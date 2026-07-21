using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GalgameUiTranslator
{
    public sealed class VisionApiClient
    {
        private static readonly HttpClient Http = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(3)
        };

        public async Task<ApiAnalysisResult> AnalyzeAsync(
            string imagePath,
            int imageWidth,
            int imageHeight,
            AppSettings settings,
            string apiKey,
            IReadOnlyCollection<GlossaryEntry> glossary,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(settings.VisionApiBaseUrl))
            {
                throw new InvalidOperationException("请先设置 API 地址。");
            }

            if (string.IsNullOrWhiteSpace(settings.VisionModel))
            {
                throw new InvalidOperationException("请先设置视觉模型名称。");
            }

            var pngBytes = ConvertToPng(imagePath);
            var dataUrl = "data:image/png;base64," + Convert.ToBase64String(pngBytes);
            var prompt = BuildPrompt(
                imageWidth,
                imageHeight,
                settings.TranslationInstructions,
                glossary);

            var payload = new
            {
                model = settings.VisionModel,
                temperature = 0.1,
                messages = new object[]
                {
                    new
                    {
                        role = "system",
                        content = "你是游戏 UI 本地化助手。严格识别图片中真正可翻译的日文文字，并只返回符合要求的 JSON。"
                    },
                    new
                    {
                        role = "user",
                        content = new object[]
                        {
                            new { type = "text", text = prompt },
                            new
                            {
                                type = "image_url",
                                image_url = new { url = dataUrl, detail = "high" }
                            }
                        }
                    }
                }
            };

            var body = JsonSerializer.Serialize(payload);
            using (var request = new HttpRequestMessage(HttpMethod.Post, BuildEndpoint(settings.VisionApiBaseUrl)))
            {
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                if (!string.IsNullOrWhiteSpace(apiKey))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
                }

                using (var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false))
                {
                    var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new InvalidOperationException(
                            $"API 请求失败：{(int)response.StatusCode} {response.ReasonPhrase}\r\n{Limit(responseBody, 1200)}");
                    }

                    var content = ExtractAssistantContent(responseBody);
                    return new ApiAnalysisResult
                    {
                        Regions = ParseRegions(content, imageWidth, imageHeight),
                        RawResponse = content,
                        ProviderName = "云端视觉 API"
                    };
                }
            }
        }

        public async Task<Dictionary<string, string>> TranslateAsync(
            IReadOnlyCollection<TextRegion> regions,
            AppSettings settings,
            string apiKey,
            IReadOnlyCollection<GlossaryEntry> glossary,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(settings.TranslationApiBaseUrl))
            {
                throw new InvalidOperationException("请先设置翻译 API 地址。");
            }

            if (string.IsNullOrWhiteSpace(settings.TranslationModel))
            {
                throw new InvalidOperationException("请先设置翻译模型名称。");
            }

            var items = regions
                .Where(region => !string.IsNullOrWhiteSpace(region.SourceText))
                .Select(region => new { id = region.Id, source = region.SourceText })
                .ToArray();
            if (items.Length == 0)
            {
                return new Dictionary<string, string>();
            }

            var inputJson = JsonSerializer.Serialize(items);
            var payload = new
            {
                model = settings.TranslationModel,
                temperature = 0.2,
                max_tokens = 4096,
                response_format = new { type = "json_object" },
                messages = new object[]
                {
                    new
                    {
                        role = "system",
                        content = "你是 Galgame UI 汉化译者。只输出 JSON，并保持每个输入 id 原样不变。"
                    },
                    new
                    {
                        role = "user",
                        content = "将下面每项 source 从日文翻译为简体中文。\n" +
                                  "要求：" + settings.TranslationInstructions + "\n" +
                                  BuildGlossaryInstructions(glossary) +
                                  "输出 JSON 格式：{\"translations\":[{\"id\":\"原 id\",\"translation\":\"中文\"}]}\n" +
                                  "输入：" + inputJson
                    }
                }
            };

            using (var request = new HttpRequestMessage(
                HttpMethod.Post,
                BuildEndpoint(settings.TranslationApiBaseUrl)))
            {
                request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                if (!string.IsNullOrWhiteSpace(apiKey))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
                }

                using (var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false))
                {
                    var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new InvalidOperationException(
                            $"翻译 API 请求失败：{(int)response.StatusCode} {response.ReasonPhrase}\r\n{Limit(responseBody, 1200)}");
                    }

                    return ParseTranslations(ExtractAssistantContent(responseBody));
                }
            }
        }

        private static string BuildPrompt(
            int width,
            int height,
            string instructions,
            IReadOnlyCollection<GlossaryEntry> glossary)
        {
            return "分析这张 " + width + "x" + height + " 像素的游戏 UI 图片。\n" +
                   "任务：\n" +
                   "1. 找出所有需要汉化的日文文本，不要识别纯装饰、图标或已经是中文/英文的内容。\n" +
                   "2. 将每段日文翻译成简体中文。翻译要求：" + instructions + "\n" +
                   BuildGlossaryInstructions(glossary) +
                   "3. 给出紧贴原文字形外缘的像素坐标，坐标原点在左上角。不要使用归一化坐标。\n" +
                   "4. 估计文字主色、描边色、水平对齐方式和合适的中文初始字号。\n\n" +
                   "只返回以下 JSON，不要添加 Markdown：\n" +
                   "{\n" +
                   "  \"image_width\": " + width + ",\n" +
                   "  \"image_height\": " + height + ",\n" +
                   "  \"coordinate_space\": \"pixels\",\n" +
                   "  \"regions\": [\n" +
                   "    {\"x\":10,\"y\":20,\"width\":120,\"height\":36," +
                   "\"source\":\"日文原文\",\"translation\":\"中文译文\"," +
                   "\"confidence\":0.95,\"text_color\":\"#FFFFFF\"," +
                   "\"outline_color\":\"#000000\",\"alignment\":\"Center\",\"font_size\":24}\n" +
                   "  ]\n" +
                   "}";
        }

        private static string BuildGlossaryInstructions(IEnumerable<GlossaryEntry> glossary)
        {
            var entries = (glossary ?? Enumerable.Empty<GlossaryEntry>())
                .Where(entry => entry != null &&
                                !string.IsNullOrWhiteSpace(entry.Source) &&
                                !string.IsNullOrWhiteSpace(entry.Translation))
                .Take(200)
                .ToList();
            if (entries.Count == 0) return string.Empty;

            var builder = new StringBuilder("相关术语必须优先采用以下译法（仅在语义适用时使用）：\n");
            foreach (var entry in entries)
            {
                builder.Append("- ")
                    .Append(CleanPromptLine(entry.Source))
                    .Append(" => ")
                    .Append(CleanPromptLine(entry.Translation));
                if (!string.IsNullOrWhiteSpace(entry.Note))
                    builder.Append("（").Append(CleanPromptLine(entry.Note)).Append('）');
                builder.Append('\n');
            }
            return builder.ToString();
        }

        private static string CleanPromptLine(string value)
        {
            var text = (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
            return text.Length <= 160 ? text : text.Substring(0, 160);
        }

        private static string BuildEndpoint(string baseUrl)
        {
            var value = baseUrl.Trim().TrimEnd('/');
            if (value.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }

            return value + "/chat/completions";
        }

        private static byte[] ConvertToPng(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var source = Image.FromStream(stream, false, false))
            using (var bitmap = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb))
            using (var graphics = Graphics.FromImage(bitmap))
            using (var output = new MemoryStream())
            {
                graphics.DrawImageUnscaled(source, 0, 0);
                bitmap.Save(output, ImageFormat.Png);
                return output.ToArray();
            }
        }

        private static string ExtractAssistantContent(string responseBody)
        {
            using (var document = JsonDocument.Parse(responseBody))
            {
                var root = document.RootElement;
                if (!TryGetIgnoreCase(root, "choices", out var choices) ||
                    choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
                {
                    throw new InvalidDataException("API 响应中缺少 choices。");
                }

                var first = choices[0];
                if (!TryGetIgnoreCase(first, "message", out var message) ||
                    !TryGetIgnoreCase(message, "content", out var content))
                {
                    throw new InvalidDataException("API 响应中缺少 message.content。");
                }

                if (content.ValueKind == JsonValueKind.String)
                {
                    return content.GetString() ?? string.Empty;
                }

                if (content.ValueKind == JsonValueKind.Array)
                {
                    var pieces = new List<string>();
                    foreach (var item in content.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String)
                        {
                            pieces.Add(item.GetString() ?? string.Empty);
                        }
                        else if (TryGetIgnoreCase(item, "text", out var text) && text.ValueKind == JsonValueKind.String)
                        {
                            pieces.Add(text.GetString() ?? string.Empty);
                        }
                    }

                    return string.Join("\n", pieces);
                }

                throw new InvalidDataException("API 返回了无法识别的 content 格式。");
            }
        }

        private static List<TextRegion> ParseRegions(string content, int imageWidth, int imageHeight)
        {
            var json = ExtractJson(content);
            using (var document = JsonDocument.Parse(json))
            {
                var root = document.RootElement;
                JsonElement regions;
                if (root.ValueKind == JsonValueKind.Array)
                {
                    regions = root;
                }
                else if (!TryGetIgnoreCase(root, "regions", out regions) || regions.ValueKind != JsonValueKind.Array)
                {
                    throw new InvalidDataException("AI 返回的 JSON 中没有 regions 数组。");
                }

                var result = new List<TextRegion>();
                foreach (var item in regions.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var x = ReadInt(item, "x", 0);
                    var y = ReadInt(item, "y", 0);
                    var width = ReadInt(item, "width", ReadInt(item, "w", 0));
                    var height = ReadInt(item, "height", ReadInt(item, "h", 0));

                    if ((width <= 0 || height <= 0) && TryGetIgnoreCase(item, "bounding_box", out var box) &&
                        box.ValueKind == JsonValueKind.Array && box.GetArrayLength() >= 4)
                    {
                        x = ReadArrayInt(box, 0);
                        y = ReadArrayInt(box, 1);
                        width = ReadArrayInt(box, 2);
                        height = ReadArrayInt(box, 3);
                    }

                    x = Clamp(x, 0, Math.Max(0, imageWidth - 1));
                    y = Clamp(y, 0, Math.Max(0, imageHeight - 1));
                    width = Clamp(width, 1, Math.Max(1, imageWidth - x));
                    height = Clamp(height, 1, Math.Max(1, imageHeight - y));

                    var source = ReadString(item, "source",
                        ReadString(item, "original", ReadString(item, "text", string.Empty)));
                    var translation = ReadString(item, "translation",
                        ReadString(item, "translated_text", string.Empty));
                    if (string.IsNullOrWhiteSpace(source) && string.IsNullOrWhiteSpace(translation))
                    {
                        continue;
                    }

                    var confidence = ReadFloat(item, "confidence", 0.8f);
                    if (confidence > 1f)
                    {
                        confidence /= 100f;
                    }

                    result.Add(new TextRegion
                    {
                        X = x,
                        Y = y,
                        Width = width,
                        Height = height,
                        SourceText = source,
                        Translation = translation,
                        Confidence = Math.Max(0f, Math.Min(1f, confidence)),
                        FontSize = Math.Max(7f, ReadFloat(item, "font_size", Math.Max(12f, height * 0.7f))),
                        TextColorArgb = ParseColor(ReadString(item, "text_color", "#FFFFFF"), Color.White).ToArgb(),
                        OutlineColorArgb = ParseColor(ReadString(item, "outline_color", "#000000"), Color.Black).ToArgb(),
                        HorizontalAlignment = NormalizeAlignment(ReadString(item, "alignment", "Center"))
                    });
                }

                return result;
            }
        }

        private static Dictionary<string, string> ParseTranslations(string content)
        {
            using (var document = JsonDocument.Parse(ExtractJson(content)))
            {
                var root = document.RootElement;
                JsonElement translations;
                if (root.ValueKind == JsonValueKind.Array)
                {
                    translations = root;
                }
                else if (!TryGetIgnoreCase(root, "translations", out translations) ||
                         translations.ValueKind != JsonValueKind.Array)
                {
                    throw new InvalidDataException("翻译 API 返回的 JSON 中没有 translations 数组。");
                }

                var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in translations.EnumerateArray())
                {
                    var id = ReadString(item, "id", string.Empty);
                    var translation = ReadString(item, "translation", string.Empty);
                    if (!string.IsNullOrWhiteSpace(id))
                    {
                        result[id] = translation;
                    }
                }

                return result;
            }
        }

        private static string ExtractJson(string content)
        {
            var trimmed = content.Trim();
            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                var firstLine = trimmed.IndexOf('\n');
                var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
                if (firstLine >= 0 && lastFence > firstLine)
                {
                    trimmed = trimmed.Substring(firstLine + 1, lastFence - firstLine - 1).Trim();
                }
            }

            var objectStart = trimmed.IndexOf('{');
            var arrayStart = trimmed.IndexOf('[');
            var start = objectStart < 0 ? arrayStart : arrayStart < 0 ? objectStart : Math.Min(objectStart, arrayStart);
            var end = trimmed.LastIndexOf(start >= 0 && trimmed[start] == '[' ? ']' : '}');
            if (start < 0 || end <= start)
            {
                throw new InvalidDataException("AI 响应中未找到有效 JSON。原始响应：\r\n" + Limit(content, 800));
            }

            return trimmed.Substring(start, end - start + 1);
        }

        private static bool TryGetIgnoreCase(JsonElement element, string name, out JsonElement value)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in element.EnumerateObject())
                {
                    if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        value = property.Value;
                        return true;
                    }
                }
            }

            value = default;
            return false;
        }

        private static string ReadString(JsonElement element, string name, string fallback)
        {
            return TryGetIgnoreCase(element, name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? fallback
                : fallback;
        }

        private static int ReadInt(JsonElement element, string name, int fallback)
        {
            if (!TryGetIgnoreCase(element, name, out var value))
            {
                return fallback;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
            {
                return number;
            }

            return value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number)
                ? number
                : fallback;
        }

        private static int ReadArrayInt(JsonElement array, int index)
        {
            var value = array[index];
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
            {
                return number;
            }

            return 0;
        }

        private static float ReadFloat(JsonElement element, string name, float fallback)
        {
            if (!TryGetIgnoreCase(element, name, out var value))
            {
                return fallback;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetSingle(out var number))
            {
                return number;
            }

            return value.ValueKind == JsonValueKind.String && float.TryParse(value.GetString(), out number)
                ? number
                : fallback;
        }

        private static Color ParseColor(string value, Color fallback)
        {
            try
            {
                if (value.StartsWith("#", StringComparison.Ordinal))
                {
                    return ColorTranslator.FromHtml(value);
                }

                return Color.FromName(value);
            }
            catch
            {
                return fallback;
            }
        }

        private static string NormalizeAlignment(string value)
        {
            if (value.Equals("Left", StringComparison.OrdinalIgnoreCase)) return "Left";
            if (value.Equals("Right", StringComparison.OrdinalIgnoreCase)) return "Right";
            return "Center";
        }

        private static int Clamp(int value, int min, int max)
        {
            return Math.Max(min, Math.Min(max, value));
        }

        private static string Limit(string value, int length)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= length)
            {
                return value;
            }

            return value.Substring(0, length) + "...";
        }
    }
}
