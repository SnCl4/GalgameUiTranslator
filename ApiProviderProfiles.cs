using System;
using System.Collections.Generic;
using System.Linq;

namespace GalgameUiTranslator
{
    public enum ApiProviderKind
    {
        OpenAiCompatible,
        Gemini,
        DeepSeek
    }

    public sealed class ApiProviderPreset
    {
        public string Id { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string BaseUrl { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public ApiProviderKind Provider { get; set; }
        public bool IsCustom { get; set; }

        public override string ToString() => DisplayName;
    }

    public static class ApiProviderProfiles
    {
        private static readonly ApiProviderPreset[] VisionProfiles =
        {
            new ApiProviderPreset
            {
                Id = "custom-vision",
                DisplayName = "自定义 / OpenAI 兼容",
                Provider = ApiProviderKind.OpenAiCompatible,
                IsCustom = true
            },
            new ApiProviderPreset
            {
                Id = "gemini-3.7-flash-vision",
                DisplayName = "Gemini 3.7 Flash（视觉）",
                BaseUrl = "https://generativelanguage.googleapis.com/v1beta/openai",
                Model = "gemini-3.7-flash",
                Provider = ApiProviderKind.Gemini
            },
            new ApiProviderPreset
            {
                Id = "deepseek-v4-flash-vision-exp",
                DisplayName = "DeepSeek V4 Flash Vision Exp（实验）",
                BaseUrl = "https://api.deepseek.com",
                Model = "deepseek-v4-flash-vision-exp",
                Provider = ApiProviderKind.DeepSeek
            }
        };

        private static readonly ApiProviderPreset[] TranslationProfiles =
        {
            new ApiProviderPreset
            {
                Id = "custom-translation",
                DisplayName = "自定义 / OpenAI 兼容",
                Provider = ApiProviderKind.OpenAiCompatible,
                IsCustom = true
            },
            new ApiProviderPreset
            {
                Id = "deepseek-v4-flash",
                DisplayName = "DeepSeek V4 Flash",
                BaseUrl = "https://api.deepseek.com",
                Model = "deepseek-v4-flash",
                Provider = ApiProviderKind.DeepSeek
            },
            new ApiProviderPreset
            {
                Id = "deepseek-v4-pro",
                DisplayName = "DeepSeek V4 Pro",
                BaseUrl = "https://api.deepseek.com",
                Model = "deepseek-v4-pro",
                Provider = ApiProviderKind.DeepSeek
            },
            new ApiProviderPreset
            {
                Id = "gemini-3.7-flash-translation",
                DisplayName = "Gemini 3.7 Flash",
                BaseUrl = "https://generativelanguage.googleapis.com/v1beta/openai",
                Model = "gemini-3.7-flash",
                Provider = ApiProviderKind.Gemini
            }
        };

        public static IReadOnlyList<ApiProviderPreset> Vision => VisionProfiles;
        public static IReadOnlyList<ApiProviderPreset> Translation => TranslationProfiles;

        public static ApiProviderKind Detect(string baseUrl, string model)
        {
            var url = baseUrl ?? string.Empty;
            var modelName = model ?? string.Empty;
            if (url.IndexOf("googleapis.com", StringComparison.OrdinalIgnoreCase) >= 0 ||
                modelName.StartsWith("gemini-", StringComparison.OrdinalIgnoreCase))
            {
                return ApiProviderKind.Gemini;
            }

            if (url.IndexOf("deepseek.com", StringComparison.OrdinalIgnoreCase) >= 0 ||
                modelName.StartsWith("deepseek-", StringComparison.OrdinalIgnoreCase))
            {
                return ApiProviderKind.DeepSeek;
            }

            return ApiProviderKind.OpenAiCompatible;
        }

        public static string GetProviderName(string baseUrl, string model)
        {
            switch (Detect(baseUrl, model))
            {
                case ApiProviderKind.Gemini:
                    return "Gemini";
                case ApiProviderKind.DeepSeek:
                    return "DeepSeek";
                default:
                    return "OpenAI 兼容 API";
            }
        }

        public static ApiProviderPreset Match(
            IEnumerable<ApiProviderPreset> profiles,
            string baseUrl,
            string model)
        {
            var normalizedUrl = NormalizeUrl(baseUrl);
            var match = (profiles ?? Enumerable.Empty<ApiProviderPreset>())
                .FirstOrDefault(profile => !profile.IsCustom &&
                    string.Equals(NormalizeUrl(profile.BaseUrl), normalizedUrl, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(profile.Model, model, StringComparison.OrdinalIgnoreCase));
            return match ?? (profiles ?? Enumerable.Empty<ApiProviderPreset>()).FirstOrDefault(profile => profile.IsCustom);
        }

        private static string NormalizeUrl(string value)
        {
            return (value ?? string.Empty).Trim().TrimEnd('/');
        }
    }
}
