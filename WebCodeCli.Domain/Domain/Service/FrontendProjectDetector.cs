using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WebCodeCli.Domain.Common.Extensions;
using WebCodeCli.Domain.Domain.Model;

namespace WebCodeCli.Domain.Domain.Service;

/// <summary>
/// 前端项目检测服务实现
/// </summary>
[ServiceDescription(typeof(IFrontendProjectDetector), ServiceLifetime.Singleton)]
public class FrontendProjectDetector : IFrontendProjectDetector
{
    private readonly ILogger<FrontendProjectDetector> _logger;

    public FrontendProjectDetector(ILogger<FrontendProjectDetector> logger)
    {
        _logger = logger;
    }

    public async Task<List<FrontendProjectInfo>> DetectProjectsAsync(string workspacePath)
    {
        var projects = new List<FrontendProjectInfo>();

        try
        {
            if (!Directory.Exists(workspacePath))
            {
                _logger.LogWarning("工作区路径不存在: {WorkspacePath}", workspacePath);
                return projects;
            }

            // 搜索所有 package.json 文件
            var packageJsonFiles = Directory.GetFiles(workspacePath, "package.json", SearchOption.AllDirectories);

            foreach (var packageJsonPath in packageJsonFiles)
            {
                // 跳过 node_modules 目录
                if (packageJsonPath.Contains("node_modules"))
                    continue;

                var projectDir = Path.GetDirectoryName(packageJsonPath);
                if (string.IsNullOrEmpty(projectDir))
                    continue;

                var projectInfo = await DetectSingleProjectAsync(projectDir);
                if (projectInfo != null)
                {
                    // 计算相对路径
                    projectInfo.RelativePath = Path.GetRelativePath(workspacePath, projectDir);
                    projectInfo.Key = projectInfo.RelativePath.Replace("\\", "/");
                    projects.Add(projectInfo);
                }
            }

            _logger.LogInformation("在工作区 {WorkspacePath} 中检测到 {Count} 个前端项目", workspacePath, projects.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "检测前端项目时发生错误");
        }

        return projects;
    }

    public async Task<FrontendProjectInfo?> DetectSingleProjectAsync(string projectPath)
    {
        try
        {
            var packageJsonPath = Path.Combine(projectPath, "package.json");
            if (!File.Exists(packageJsonPath))
                return null;

            var packageJsonContent = await File.ReadAllTextAsync(packageJsonPath);
            using var packageJson = JsonDocument.Parse(packageJsonContent);
            var root = packageJson.RootElement;

            // 获取项目名称
            var projectName = root.TryGetProperty("name", out var nameElement) 
                ? nameElement.GetString() ?? Path.GetFileName(projectPath) 
                : Path.GetFileName(projectPath);

            // 检测项目类型
            var projectType = DetectProjectType(root, projectPath);
            if (projectType == FrontendProjectType.Unknown)
                return null;

            // 检测包管理器
            var packageManager = DetectPackageManager(projectPath);

            // 检查是否需要安装依赖
            var needsInstall = !Directory.Exists(Path.Combine(projectPath, "node_modules"));

            // 获取脚本命令
            var (devCommand, buildCommand) = GetScriptCommands(root, projectType);

            // 获取构建输出目录
            var buildOutputDir = GetBuildOutputDir(projectType, projectPath);

            // 尝试读取默认端口
            var defaultPort = await DetectDefaultPortAsync(projectPath, projectType);

            return new FrontendProjectInfo
            {
                Name = projectName,
                Type = projectType,
                AbsolutePath = projectPath,
                RelativePath = string.Empty, // 将在 DetectProjectsAsync 中设置
                Key = string.Empty, // 将在 DetectProjectsAsync 中设置
                DevCommand = devCommand,
                BuildCommand = buildCommand,
                BuildOutputDir = buildOutputDir,
                NeedsDependencyInstall = needsInstall,
                PackageManager = packageManager,
                DefaultPort = defaultPort
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "检测单个项目时发生错误: {ProjectPath}", projectPath);
            return null;
        }
    }

    public string GetProjectTypeName(FrontendProjectType type)
    {
        return type switch
        {
            FrontendProjectType.VueVite => "Vue + Vite",
            FrontendProjectType.ReactVite => "React + Vite",
            FrontendProjectType.NextJs => "Next.js",
            FrontendProjectType.NuxtJs => "Nuxt.js",
            _ => "Unknown"
        };
    }

    private FrontendProjectType DetectProjectType(JsonElement packageJson, string projectPath)
    {
        var dependencies = new Dictionary<string, bool>();
        var devDependencies = new Dictionary<string, bool>();

        // 读取依赖
        if (packageJson.TryGetProperty("dependencies", out var deps))
        {
            foreach (var prop in deps.EnumerateObject())
            {
                dependencies[prop.Name] = true;
            }
        }

        if (packageJson.TryGetProperty("devDependencies", out var devDeps))
        {
            foreach (var prop in devDeps.EnumerateObject())
            {
                devDependencies[prop.Name] = true;
            }
        }

        // 检测 Next.js
        if (dependencies.ContainsKey("next") || devDependencies.ContainsKey("next"))
        {
            return FrontendProjectType.NextJs;
        }

        // 检测 Nuxt.js
        if (dependencies.ContainsKey("nuxt") || devDependencies.ContainsKey("nuxt"))
        {
            return FrontendProjectType.NuxtJs;
        }

        // 检测 Vite 项目
        var hasVite = dependencies.ContainsKey("vite") || devDependencies.ContainsKey("vite");
        if (hasVite)
        {
            // 区分 Vue 和 React
            if (dependencies.ContainsKey("vue") || devDependencies.ContainsKey("vue"))
            {
                return FrontendProjectType.VueVite;
            }

            if (dependencies.ContainsKey("react") || devDependencies.ContainsKey("react"))
            {
                return FrontendProjectType.ReactVite;
            }
        }

        // 检查配置文件
        if (File.Exists(Path.Combine(projectPath, "vite.config.js")) ||
            File.Exists(Path.Combine(projectPath, "vite.config.ts")))
        {
            // 通过文件内容进一步判断
            if (dependencies.ContainsKey("vue"))
                return FrontendProjectType.VueVite;
            if (dependencies.ContainsKey("react"))
                return FrontendProjectType.ReactVite;
        }

        return FrontendProjectType.Unknown;
    }

    private string DetectPackageManager(string projectPath)
    {
        // 检查锁文件来确定包管理器
        if (File.Exists(Path.Combine(projectPath, "pnpm-lock.yaml")))
            return "pnpm";
        if (File.Exists(Path.Combine(projectPath, "yarn.lock")))
            return "yarn";
        if (File.Exists(Path.Combine(projectPath, "package-lock.json")))
            return "npm";

        return "npm"; // 默认使用 npm
    }

    private (string devCommand, string buildCommand) GetScriptCommands(JsonElement packageJson, FrontendProjectType type)
    {
        var devCommand = "dev";
        var buildCommand = "build";

        // 尝试从 package.json scripts 中读取
        if (packageJson.TryGetProperty("scripts", out var scripts))
        {
            // 优先查找常见的开发命令
            if (scripts.TryGetProperty("dev", out _))
                devCommand = "dev";
            else if (scripts.TryGetProperty("serve", out _))
                devCommand = "serve";
            else if (scripts.TryGetProperty("start", out _))
                devCommand = "start";

            // 查找构建命令
            if (scripts.TryGetProperty("build", out _))
                buildCommand = "build";
        }

        return (devCommand, buildCommand);
    }

    private string GetBuildOutputDir(FrontendProjectType type, string projectPath)
    {
        return type switch
        {
            FrontendProjectType.VueVite or FrontendProjectType.ReactVite => "dist",
            FrontendProjectType.NextJs => ".next",
            FrontendProjectType.NuxtJs => ".output/public",
            _ => "dist"
        };
    }

    private async Task<int?> DetectDefaultPortAsync(string projectPath, FrontendProjectType type)
    {
        try
        {
            // 尝试从 vite.config.js/ts 读取端口
            if (type == FrontendProjectType.VueVite || type == FrontendProjectType.ReactVite)
            {
                var viteConfigFiles = new[] { "vite.config.js", "vite.config.ts", "vite.config.mjs" };
                foreach (var configFile in viteConfigFiles)
                {
                    var configPath = Path.Combine(projectPath, configFile);
                    if (File.Exists(configPath))
                    {
                        var content = await File.ReadAllTextAsync(configPath);
                        // 简单的正则匹配 port: 3000 或 port:3000
                        var match = System.Text.RegularExpressions.Regex.Match(content, @"port\s*:\s*(\d+)");
                        if (match.Success && int.TryParse(match.Groups[1].Value, out var port))
                        {
                            return port;
                        }
                    }
                }
            }

            // Next.js 和 Nuxt.js 的默认端口
            if (type == FrontendProjectType.NextJs)
                return 3000;
            if (type == FrontendProjectType.NuxtJs)
                return 3000;

            // Vite 默认端口
            return 5173;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "检测默认端口失败: {ProjectPath}", projectPath);
            return null;
        }
    }
}

