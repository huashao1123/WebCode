using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using System.Text.Json;
using System.Text.RegularExpressions;
using WebCodeCli.Domain.Domain.Model;
using WebCodeCli.Domain.Domain.Service;

namespace WebCodeCli.Components;

public partial class TemplateLibraryModal : ComponentBase
{
    [Inject] private IJSRuntime JSRuntime { get; set; } = default!;
    [Inject] private ILocalizationService L { get; set; } = default!;
    [Inject] private IPromptTemplateService PromptTemplateService { get; set; } = default!;

    [Parameter] public EventCallback<PromptTemplate> OnTemplateSelected { get; set; }
    [Parameter] public bool ShowButton { get; set; } = true;
    [Parameter] public string ButtonClass { get; set; } = "btn-default flex items-center gap-2";
    [Parameter] public string ButtonTextClass { get; set; } = "hidden sm:inline";

    private List<PromptTemplate> _templates = new();
    private List<PromptTemplate> _filteredTemplates = new();
    private bool _showModal = false;
    private bool _showTemplateDialog = false;
    private bool _isLoading = false;
    private string _searchText = string.Empty;
    private string _selectedCategory = string.Empty;
    private PromptTemplate? _editingTemplate = null;
    private TemplateFormModel _templateForm = new();

    // 本地化相关
    private Dictionary<string, string> _translations = new();
    private string _currentLanguage = "zh-CN";

    private string LocalizedTitle => T("templateLibrary.title");
    private string LocalizedSearch => T("templateLibrary.search");

    protected override async Task OnInitializedAsync()
    {
        await LoadTranslationsAsync();
    }

    public async Task OpenModal()
    {
        _showModal = true;
        await LoadTemplatesAsync();
    }

    private void CloseModal()
    {
        _showModal = false;
        _searchText = string.Empty;
        _selectedCategory = string.Empty;
    }

    private async Task LoadTemplatesAsync()
    {
        _isLoading = true;
        StateHasChanged();

        try
        {
            // 从后端加载模板
            var templates = await PromptTemplateService.GetAllAsync();
            if (templates != null && templates.Count > 0)
            {
                _templates = templates;
            }
            else
            {
                // 如果后端没有模板，初始化默认模板
                await PromptTemplateService.InitDefaultTemplatesAsync();
                _templates = await PromptTemplateService.GetAllAsync();
            }

            FilterTemplates();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"加载模板失败: {ex.Message}");
            _templates = new List<PromptTemplate>();
        }
        finally
        {
            _isLoading = false;
            StateHasChanged();
        }
    }

    private void FilterTemplates()
    {
        var query = _templates.AsEnumerable();

        // 按分类筛选
        if (!string.IsNullOrEmpty(_selectedCategory))
        {
            query = query.Where(t => t.Category == _selectedCategory);
        }

        // 按搜索文本筛选
        if (!string.IsNullOrWhiteSpace(_searchText))
        {
            var searchLower = _searchText.ToLower();
            query = query.Where(t => 
                t.Title.ToLower().Contains(searchLower) || 
                t.Content.ToLower().Contains(searchLower) ||
                t.Description.ToLower().Contains(searchLower));
        }

        // 排序：收藏的在前，然后按使用次数
        _filteredTemplates = query
            .OrderByDescending(t => t.IsFavorite)
            .ThenByDescending(t => t.UsageCount)
            .ThenByDescending(t => t.UpdatedAt)
            .ToList();

        StateHasChanged();
    }

    private async Task ToggleFavorite(PromptTemplate template)
    {
        template.IsFavorite = !template.IsFavorite;
        template.UpdatedAt = DateTime.Now;
        
        await SaveTemplateAsync(template);
        FilterTemplates();
    }

    private async Task UseTemplate(PromptTemplate template)
    {
        template.UsageCount++;
        template.UpdatedAt = DateTime.Now;
        
        await SaveTemplateAsync(template);
        
        if (OnTemplateSelected.HasDelegate)
        {
            await OnTemplateSelected.InvokeAsync(template);
        }

        CloseModal();
    }

    private void ShowAddTemplateDialog()
    {
        _editingTemplate = null;
        _templateForm = new TemplateFormModel
        {
            Icon = "⭐",
            Category = "custom"
        };
        _showTemplateDialog = true;
    }

    private void ShowEditTemplateDialog(PromptTemplate template)
    {
        _editingTemplate = template;
        _templateForm = new TemplateFormModel
        {
            Title = template.Title,
            Content = template.Content,
            Icon = template.Icon,
            Category = template.Category,
            Description = template.Description
        };
        _showTemplateDialog = true;
    }

    private void CloseTemplateDialog()
    {
        _showTemplateDialog = false;
        _editingTemplate = null;
        _templateForm = new TemplateFormModel();
    }

    private async Task SaveTemplate()
    {
        if (string.IsNullOrWhiteSpace(_templateForm.Title) || 
            string.IsNullOrWhiteSpace(_templateForm.Content) ||
            string.IsNullOrWhiteSpace(_templateForm.Icon))
        {
            return;
        }

        // 提取模板中的变量
        var variables = ExtractVariables(_templateForm.Content);

        if (_editingTemplate != null)
        {
            // 编辑现有模板
            _editingTemplate.Title = _templateForm.Title;
            _editingTemplate.Content = _templateForm.Content;
            _editingTemplate.Icon = _templateForm.Icon;
            _editingTemplate.Category = _templateForm.Category;
            _editingTemplate.Description = _templateForm.Description;
            _editingTemplate.Variables = variables;
            _editingTemplate.UpdatedAt = DateTime.Now;
            
            await SaveTemplateAsync(_editingTemplate);
        }
        else
        {
            // 添加新模板
            var newTemplate = new PromptTemplate
            {
                Id = Guid.NewGuid().ToString(),
                Title = _templateForm.Title,
                Content = _templateForm.Content,
                Icon = _templateForm.Icon,
                Category = _templateForm.Category,
                Description = _templateForm.Description,
                Variables = variables,
                IsCustom = true,
                IsFavorite = false,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now,
                UsageCount = 0
            };

            await SaveTemplateAsync(newTemplate);
            _templates.Add(newTemplate);
        }

        CloseTemplateDialog();
        FilterTemplates();
    }

    private async Task SaveTemplateAsync(PromptTemplate template)
    {
        try
        {
            await PromptTemplateService.SaveAsync(template);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"保存模板失败: {ex.Message}");
        }
    }

    private async Task DeleteTemplate(PromptTemplate template)
    {
        if (!template.IsCustom)
        {
            return;
        }

        try
        {
            await PromptTemplateService.DeleteAsync(template.Id);
            _templates.Remove(template);
            FilterTemplates();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"删除模板失败: {ex.Message}");
        }
    }

    private async Task ExportTemplates()
    {
        try
        {
            var json = JsonSerializer.Serialize(_templates, new JsonSerializerOptions { WriteIndented = true });
            var fileName = $"templates_{DateTime.Now:yyyyMMdd_HHmmss}.json";
            
            await JSRuntime.InvokeVoidAsync("downloadFile", fileName, json, "application/json");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"导出模板失败: {ex.Message}");
        }
    }

    private async Task ImportTemplates(InputFileChangeEventArgs e)
    {
        try
        {
            var file = e.File;
            if (file.ContentType != "application/json")
            {
                return;
            }

            using var stream = file.OpenReadStream(maxAllowedSize: 5 * 1024 * 1024); // 5MB
            using var reader = new StreamReader(stream);
            var json = await reader.ReadToEndAsync();
            
            var importedTemplates = JsonSerializer.Deserialize<List<PromptTemplate>>(json);
            if (importedTemplates != null && importedTemplates.Any())
            {
                foreach (var template in importedTemplates)
                {
                    // 确保ID唯一
                    template.Id = Guid.NewGuid().ToString();
                    template.IsCustom = true;
                    template.CreatedAt = DateTime.Now;
                    template.UpdatedAt = DateTime.Now;
                    
                    await SaveTemplateAsync(template);
                    _templates.Add(template);
                }

                FilterTemplates();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"导入模板失败: {ex.Message}");
        }
    }

    private List<string> ExtractVariables(string content)
    {
        var variables = new List<string>();
        var regex = new Regex(@"\{\{(\w+)\}\}");
        var matches = regex.Matches(content);

        foreach (Match match in matches)
        {
            var variable = match.Groups[1].Value;
            if (!variables.Contains(variable))
            {
                variables.Add(variable);
            }
        }

        return variables;
    }

    private string GetCategoryName(string category)
    {
        return T($"templateLibrary.categories.{category}");
    }

    #region 本地化辅助方法

    /// <summary>
    /// 加载翻译资源
    /// </summary>
    private async Task LoadTranslationsAsync()
    {
        try
        {
            _currentLanguage = await L.GetCurrentLanguageAsync();
            var allTranslations = await L.GetAllTranslationsAsync(_currentLanguage);
            _translations = FlattenTranslations(allTranslations);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[模板库] 加载翻译资源失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 将嵌套的翻译字典展平为点分隔的键
    /// </summary>
    private Dictionary<string, string> FlattenTranslations(Dictionary<string, object> source, string prefix = "")
    {
        var result = new Dictionary<string, string>();

        foreach (var kvp in source)
        {
            var key = string.IsNullOrEmpty(prefix) ? kvp.Key : $"{prefix}.{kvp.Key}";

            if (kvp.Value is JsonElement jsonElement)
            {
                if (jsonElement.ValueKind == JsonValueKind.Object)
                {
                    var nested = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonElement.GetRawText());
                    if (nested != null)
                    {
                        foreach (var item in FlattenTranslations(nested, key))
                        {
                            result[item.Key] = item.Value;
                        }
                    }
                }
                else if (jsonElement.ValueKind == JsonValueKind.String)
                {
                    result[key] = jsonElement.GetString() ?? key;
                }
            }
            else if (kvp.Value is Dictionary<string, object> dict)
            {
                foreach (var item in FlattenTranslations(dict, key))
                {
                    result[item.Key] = item.Value;
                }
            }
            else if (kvp.Value is string str)
            {
                result[key] = str;
            }
        }

        return result;
    }

    /// <summary>
    /// 获取翻译文本
    /// </summary>
    private string T(string key)
    {
        if (_translations.TryGetValue(key, out var translation))
        {
            return translation;
        }

        var parts = key.Split('.');
        return parts.Length > 0 ? parts[^1] : key;
    }

    /// <summary>
    /// 获取翻译文本（带参数）
    /// </summary>
    private string T(string key, params (string name, string value)[] parameters)
    {
        var text = T(key);
        foreach (var (name, value) in parameters)
        {
            text = text.Replace($"{{{name}}}", value);
        }
        return text;
    }

    #endregion

    private class TemplateFormModel
    {
        public string Title { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public string Icon { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }
}
