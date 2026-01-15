using System.Text.Json;
using Microsoft.JSInterop;

namespace WebCodeCli.Domain.Domain.Service;

/// <summary>
/// 本地化服务实现
/// </summary>
public class LocalizationService : ILocalizationService
{
    private readonly IJSRuntime _jsRuntime;
    private readonly string _resourcePath = "Resources/Localization";
    private readonly Dictionary<string, Dictionary<string, object>> _translationsCache = new();
    private string _currentLanguage = "zh-CN";

    public LocalizationService(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    public async Task<string> GetCurrentLanguageAsync()
    {
        try
        {
            var language = await _jsRuntime.InvokeAsync<string>("localizationHelper.getCurrentLanguage");
            if (!string.IsNullOrEmpty(language))
            {
                _currentLanguage = language;
            }
            return _currentLanguage;
        }
        catch
        {
            return _currentLanguage;
        }
    }

    public async Task SetCurrentLanguageAsync(string language)
    {
        try
        {
            await _jsRuntime.InvokeVoidAsync("localizationHelper.setCurrentLanguage", language);
            _currentLanguage = language;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"设置语言失败: {ex.Message}");
        }
    }

    public async Task<string> GetTranslationAsync(string key, string? language = null)
    {
        return await GetTranslationAsync(key, new Dictionary<string, string>(), language);
    }

    public async Task<string> GetTranslationAsync(string key, Dictionary<string, string> parameters, string? language = null)
    {
        try
        {
            var lang = language ?? _currentLanguage;
            var translations = await GetAllTranslationsAsync(lang);

            // 支持嵌套键（如 "common.save"）
            var keys = key.Split('.');
            object? value = translations;

            foreach (var k in keys)
            {
                if (value is Dictionary<string, object> dict && dict.ContainsKey(k))
                {
                    value = dict[k];
                }
                else
                {
                    return key; // 找不到翻译，返回键本身
                }
            }

            if (value is not string stringValue)
            {
                return key;
            }

            // 参数替换
            foreach (var param in parameters)
            {
                stringValue = stringValue.Replace($"{{{param.Key}}}", param.Value);
            }

            return stringValue;
        }
        catch
        {
            return key;
        }
    }

    public async Task<Dictionary<string, object>> GetAllTranslationsAsync(string language)
    {
        // 检查缓存
        if (_translationsCache.ContainsKey(language))
        {
            return _translationsCache[language];
        }

        try
        {
            // 使用 JavaScript fetch 来加载翻译文件
            var filePath = $"./Resources/Localization/{language}.json";
            var json = await _jsRuntime.InvokeAsync<string>("localizationHelper.fetchTranslationFile", filePath);
            
            if (string.IsNullOrEmpty(json))
            {
                Console.WriteLine($"翻译文件为空 ({language})");
                return new Dictionary<string, object>();
            }
            
            var translations = JsonSerializer.Deserialize<Dictionary<string, object>>(json, 
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) 
                ?? new Dictionary<string, object>();

            _translationsCache[language] = translations;
            
            // 同时加载到 JS 端
            await _jsRuntime.InvokeVoidAsync("localizationHelper.loadTranslations", language, translations);

            return translations;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"加载翻译资源失败 ({language}): {ex.Message}");
            return new Dictionary<string, object>();
        }
    }

    public List<LanguageInfo> GetSupportedLanguages()
    {
        return new List<LanguageInfo>
        {
            new() { Code = "zh-CN", Name = "Chinese Simplified", NativeName = "简体中文" },
            new() { Code = "en-US", Name = "English", NativeName = "English" },
            new() { Code = "ja-JP", Name = "Japanese", NativeName = "日本語" },
            new() { Code = "ko-KR", Name = "Korean", NativeName = "한국어" }
        };
    }

    public async Task ReloadTranslationsAsync()
    {
        _translationsCache.Clear();
        await GetAllTranslationsAsync(_currentLanguage);
    }
}

