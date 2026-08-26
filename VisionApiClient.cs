using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
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
        private const int CloudTileSize = 1024;
        private const int CloudTileOverlap = 96;
        private const int CloudTileThreshold = 1536;

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
            CancellationToken cancellationToken)
        {
            ValidateVisionSettings(settings);
            using (var bitmap = ImageProcessor.LoadBitmapUnlocked(imagePath))
            {
                var fullWidth = imageWidth > 0 ? imageWidth : bitmap.Width;
                var fullHeight = imageHeight > 0 ? imageHeight : bitmap.Height;
                var tiles = CreateCloudTiles(bitmap.Width, bitmap.Height, settings.CloudTilingEnabled);
                var merged = new List<TextRegion>();
                var responses = new List<string>();
                foreach (var tile in tiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var content = await SendVisionRequestAsync(
                        bitmap,
                        tile,
                        settings,
                        apiKey,
                        cancellationToken).ConfigureAwait(false);
                    responses.Add(content);
                    var tileRegions = ParseRegions(content, tile.Width, tile.Height);
                    foreach (var region in tileRegions)
                    {
                        region.X += tile.X;
                        region.Y += tile.Y;
                        region.X = Clamp(region.X, 0, Math.Max(0, fullWidth - 1));
                        region.Y = Clamp(region.Y, 0, Math.Max(0, fullHeight - 1));
                        region.Width = Clamp(region.Width, 1, Math.Max(1, fullWidth - region.X));
                        region.Height = Clamp(region.Height, 1, Math.Max(1, fullHeight - region.Y));
                    }

                    merged = TextRegionMergeService.MergeCloudTiles(
                        merged,
                        tileRegions,
                        fullWidth,
                        fullHeight);
                }

                var provider = ApiProviderProfiles.GetProviderName(
                    settings.VisionApiBaseUrl,
                    settings.VisionModel);
                return new ApiAnalysisResult
                {
                    Regions = merged,
                    RawResponse = string.Join("\r\n\r\n--- TILE ---\r\n\r\n", responses),
                    ProviderName = tiles.Count > 1
                        ? $"{provider} 云端视觉（{tiles.Count} 个分块）"
                        : provider + " 云端视觉"
                };
            }
        }

        public async Task<Dictionary<string, string>> TranslateAsync(
            IReadOnlyCollection<TextRegion> regions,
            AppSettings settings,
            string apiKey,
            IReadOnlyCollection<GlossaryEntry> glossary,
            CancellationToken cancellationToken)
        {
            ValidateTranslationSettings(settings);
            var items = regions
                .Where(region => !string.IsNullOrWhiteSpace(region.SourceText))
                .Select(region => new { id = region.Id, source = region.SourceText })
                .ToArray();
            if (items.Length == 0) return new Dictionary<string, string>();

            var payload = new Dictionary<string, object>
            {
                ["model"] = settings.TranslationModel,
                ["max_tokens"] = 4096,
                ["response_format"] = new { type = "json_object" },
                ["messages"] = new object[]
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
                                  "不要改写占位符、转义序列、数字或脚本标签。" +
                                  "输出 JSON 格式：{\"translations\":[{\"id\":\"原 id\",\"translation\":\"中文\"}]}\n" +
                                  "输入：" + JsonSerializer.Serialize(items)
                    }
                }
            };
            ApplyProviderOptions(
                payload,
                settings.TranslationApiBaseUrl,
                settings.TranslationModel,
                0.2);

            var content = await SendChatRequestAsync(
                settings.TranslationApiBaseUrl,
                payload,
                apiKey,
                "翻译 API",
                cancellationToken).ConfigureAwait(false);
            return ParseTranslations(content);
        }

        public async Task<string> TestVisionConnectionAsync(
            AppSettings settings,
            string apiKey,
            CancellationToken cancellationToken)
        {
            ValidateVisionSettings(settings);
            var payload = new Dictionary<string, object>
            {
                ["model"] = settings.VisionModel,
                ["max_tokens"] = 128,
                ["messages"] = new object[]
                {
                    new
                    {
                        role = "user",
                        content = "这是 API 连通性测试。只返回 JSON：{\"regions\":[]}"
                    }
                }
            };
            var provider = ApiProviderProfiles.Detect(settings.VisionApiBaseUrl, settings.VisionModel);
            if (provider != ApiProviderKind.OpenAiCompatible)
                payload["response_format"] = new { type = "json_object" };
            ApplyProviderOptions(payload, settings.VisionApiBaseUrl, settings.VisionModel, 0.1);
            var content = await RunConnectionTestAsync(
                token => SendChatRequestAsync(
                    settings.VisionApiBaseUrl,
                    payload,
                    apiKey,
                    "视觉 API",
                    token),
                cancellationToken).ConfigureAwait(false);
            ParseRegions(content, 1, 1);

            return ApiProviderProfiles.GetProviderName(settings.VisionApiBaseUrl, settings.VisionModel) +
                   " 接口与模型连接成功";
        }

        public async Task<string> TestTranslationConnectionAsync(
            AppSettings settings,
            string apiKey,
            CancellationToken cancellationToken)
        {
            var probe = new TextRegion { SourceText = "開始" };
            var translations = await RunConnectionTestAsync(
                token => TranslateAsync(
                    new[] { probe },
                    settings,
                    apiKey,
                    Array.Empty<GlossaryEntry>(),
                    token),
                cancellationToken).ConfigureAwait(false);
            if (!translations.TryGetValue(probe.Id, out var translated) || string.IsNullOrWhiteSpace(translated))
                throw new InvalidDataException("接口已响应，但没有返回测试译文。");
            return ApiProviderProfiles.GetProviderName(settings.TranslationApiBaseUrl, settings.TranslationModel) +
                   " 翻译接口连接成功";
        }

        public static IReadOnlyList<Rectangle> CreateCloudTiles(int width, int height, bool enabled)
        {
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
            if (!enabled || (width <= CloudTileThreshold && height <= CloudTileThreshold))
                return new[] { new Rectangle(0, 0, width, height) };

            var horizontal = BuildAxisSlices(width);
            var vertical = BuildAxisSlices(height);
            var result = new List<Rectangle>(horizontal.Count * vertical.Count);
            foreach (var y in vertical)
            foreach (var x in horizontal)
                result.Add(new Rectangle(x.Start, y.Start, x.Length, y.Length));
            return result;
        }

        private static async Task<string> SendVisionRequestAsync(
            Bitmap bitmap,
            Rectangle tile,
            AppSettings settings,
            string apiKey,
            CancellationToken cancellationToken)
        {
            var provider = ApiProviderProfiles.Detect(settings.VisionApiBaseUrl, settings.VisionModel);
            var dataUrl = "data:image/png;base64," + Convert.ToBase64String(ConvertToPng(bitmap, tile));
            var imageUrl = provider == ApiProviderKind.DeepSeek
                ? (object)new { url = dataUrl, detail = "original" }
                : new { url = dataUrl };
            var payload = new Dictionary<string, object>
            {
                ["model"] = settings.VisionModel,
                ["messages"] = new object[]
                {
                    new
                    {
                        role = "system",
                        content = "你是游戏 UI 日文文字识别器。只做识别和版式估计，不要翻译，并且只返回符合要求的 JSON。"
                    },
                    new
                    {
                        role = "user",
                        content = new object[]
                        {
                            new { type = "text", text = BuildRecognitionPrompt(tile.Width, tile.Height) },
                            new
                            {
                                type = "image_url",
                                image_url = imageUrl
                            }
                        }
                    }
                }
            };
            if (provider != ApiProviderKind.OpenAiCompatible)
                payload["response_format"] = new { type = "json_object" };
            ApplyProviderOptions(payload, settings.VisionApiBaseUrl, settings.VisionModel, 0.1);
            return await SendChatRequestAsync(
                settings.VisionApiBaseUrl,
                payload,
                apiKey,
                "视觉 API",
                cancellationToken).ConfigureAwait(false);
        }

        private static async Task<string> SendChatRequestAsync(
            string baseUrl,
            Dictionary<string, object> payload,
            string apiKey,
            string operationName,
            CancellationToken cancellationToken)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Post, BuildEndpoint(baseUrl)))
            {
                request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                if (!string.IsNullOrWhiteSpace(apiKey))
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());

                try
                {
                    using (var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false))
                    {
                        var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        if (!response.IsSuccessStatusCode)
                        {
                            throw new InvalidOperationException(
                                $"{operationName} 请求失败：{(int)response.StatusCode} {response.ReasonPhrase}\r\n" +
                                GetStatusHint((int)response.StatusCode) +
                                Limit(responseBody, 1200));
                        }
                        return ExtractAssistantContent(responseBody);
                    }
                }
                catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new TimeoutException(
                        $"{operationName} 请求超过 3 分钟仍未完成。请检查代理、网络出口或稍后重试。",
                        exception);
                }
                catch (HttpRequestException exception)
                {
                    throw new InvalidOperationException(
                        $"无法连接 {operationName}：{exception.Message}\r\n请检查 DNS、TLS、系统代理和防火墙。",
                        exception);
                }
            }
        }

        private static async Task<T> RunConnectionTestAsync<T>(
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken)
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(30));
                try
                {
                    return await action(timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new TimeoutException(
                        "连接测试在 30 秒内没有收到响应。接口不一定失效，请检查代理出口、支持地区或稍后重试。",
                        exception);
                }
            }
        }

        private static string GetStatusHint(int statusCode)
        {
            switch (statusCode)
            {
                case 400:
                    return "请求参数不受支持，请检查模型名称和 API 版本。\r\n";
                case 401:
                    return "API Key 缺失、无效或已过期。\r\n";
                case 403:
                    return "API Key、项目权限或当前网络出口地区不被允许。\r\n";
                case 404:
                    return "接口地址或模型名称不存在。\r\n";
                case 408:
                case 429:
                    return "请求超时或已达到速率/额度限制，请稍后重试。\r\n";
                default:
                    return statusCode >= 500
                        ? "服务暂时不可用，建议稍后重试。\r\n"
                        : string.Empty;
            }
        }

        private static void ApplyProviderOptions(
            IDictionary<string, object> payload,
            string baseUrl,
            string model,
            double temperature)
        {
            switch (ApiProviderProfiles.Detect(baseUrl, model))
            {
                case ApiProviderKind.Gemini:
                    payload["reasoning_effort"] = "low";
                    break;
                case ApiProviderKind.DeepSeek:
                    payload["thinking"] = new { type = "disabled" };
                    payload["temperature"] = temperature;
                    break;
                default:
                    payload["temperature"] = temperature;
                    break;
            }
        }

        private static string BuildRecognitionPrompt(int width, int height)
        {
            return "识别这张 " + width + "x" + height + " 像素的游戏 UI 图片。\n" +
                   "任务：\n" +
                   "1. 找出所有需要汉化的日文文本，不要把纯装饰、图标、中文或英文当作日文。\n" +
                   "2. source 必须逐字记录图片中的日文原文，不要翻译、改写或补全。\n" +
                   "3. 给出紧贴原文字形外缘的像素坐标，原点在左上角，不要使用归一化坐标。\n" +
                   "4. 估计主色、描边色、水平对齐、文字方向和合适的初始字号。\n\n" +
                   "只返回以下 JSON，不要添加 Markdown：\n" +
                   "{\n" +
                   "  \"regions\": [\n" +
                   "    {\"x\":10,\"y\":20,\"width\":120,\"height\":36," +
                   "\"source\":\"日文原文\",\"confidence\":0.95," +
                   "\"text_color\":\"#FFFFFF\",\"outline_color\":\"#000000\"," +
                   "\"alignment\":\"Center\",\"vertical_text\":false,\"font_size\":24}\n" +
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

        private static void ValidateVisionSettings(AppSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (string.IsNullOrWhiteSpace(settings.VisionApiBaseUrl))
                throw new InvalidOperationException("请先设置视觉 API 地址。");
            if (string.IsNullOrWhiteSpace(settings.VisionModel))
                throw new InvalidOperationException("请先设置视觉模型名称。");
        }

        private static void ValidateTranslationSettings(AppSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (string.IsNullOrWhiteSpace(settings.TranslationApiBaseUrl))
                throw new InvalidOperationException("请先设置翻译 API 地址。");
            if (string.IsNullOrWhiteSpace(settings.TranslationModel))
                throw new InvalidOperationException("请先设置翻译模型名称。");
        }

        private static List<(int Start, int Length)> BuildAxisSlices(int length)
        {
            if (length <= CloudTileThreshold) return new List<(int, int)> { (0, length) };
            var tileLength = Math.Min(CloudTileSize, length);
            var usableStep = tileLength - CloudTileOverlap;
            var count = Math.Max(2, (int)Math.Ceiling((length - CloudTileOverlap) / (double)usableStep));
            var lastStart = length - tileLength;
            var result = new List<(int Start, int Length)>(count);
            for (var index = 0; index < count; index++)
            {
                var start = (int)Math.Round(lastStart * index / (double)(count - 1));
                if (result.Count == 0 || result[result.Count - 1].Start != start)
                    result.Add((start, tileLength));
            }
            return result;
        }

        private static string BuildEndpoint(string baseUrl)
        {
            var value = (baseUrl ?? string.Empty).Trim().TrimEnd('/');
            if (value.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)) return value;
            return value + "/chat/completions";
        }

        private static byte[] ConvertToPng(Bitmap source, Rectangle tile)
        {
            using (var bitmap = new Bitmap(tile.Width, tile.Height, PixelFormat.Format32bppArgb))
            using (var graphics = Graphics.FromImage(bitmap))
            using (var output = new MemoryStream())
            {
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
                graphics.DrawImage(source, new Rectangle(0, 0, tile.Width, tile.Height), tile, GraphicsUnit.Pixel);
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
                    throw new InvalidDataException("API 响应中缺少 choices。");

                var first = choices[0];
                if (!TryGetIgnoreCase(first, "message", out var message) ||
                    !TryGetIgnoreCase(message, "content", out var content))
                    throw new InvalidDataException("API 响应中缺少 message.content。");

                if (content.ValueKind == JsonValueKind.String)
                    return content.GetString() ?? string.Empty;
                if (content.ValueKind == JsonValueKind.Array)
                {
                    var pieces = new List<string>();
                    foreach (var item in content.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String)
                            pieces.Add(item.GetString() ?? string.Empty);
                        else if (TryGetIgnoreCase(item, "text", out var text) && text.ValueKind == JsonValueKind.String)
                            pieces.Add(text.GetString() ?? string.Empty);
                    }
                    return string.Join("\n", pieces);
                }
                throw new InvalidDataException("API 返回了无法识别的 content 格式。");
            }
        }

        private static List<TextRegion> ParseRegions(string content, int imageWidth, int imageHeight)
        {
            using (var document = JsonDocument.Parse(ExtractJson(content)))
            {
                var root = document.RootElement;
                JsonElement regions;
                if (root.ValueKind == JsonValueKind.Array)
                    regions = root;
                else if (!TryGetIgnoreCase(root, "regions", out regions) || regions.ValueKind != JsonValueKind.Array)
                    throw new InvalidDataException("AI 返回的 JSON 中没有 regions 数组。");

                var result = new List<TextRegion>();
                foreach (var item in regions.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object) continue;
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
                        ReadString(item, "original", ReadString(item, "text", string.Empty))).Trim();
                    if (string.IsNullOrWhiteSpace(source)) continue;

                    var confidence = ReadFloat(item, "confidence", 0.8f);
                    if (confidence > 1f) confidence /= 100f;
                    result.Add(new TextRegion
                    {
                        X = x,
                        Y = y,
                        Width = width,
                        Height = height,
                        SourceText = source,
                        Translation = string.Empty,
                        Confidence = Math.Max(0f, Math.Min(1f, confidence)),
                        FontSize = Math.Max(7f, ReadFloat(item, "font_size", Math.Max(12f, height * 0.7f))),
                        TextColorArgb = ParseColor(ReadString(item, "text_color", "#FFFFFF"), Color.White).ToArgb(),
                        OutlineColorArgb = ParseColor(ReadString(item, "outline_color", "#000000"), Color.Black).ToArgb(),
                        HorizontalAlignment = NormalizeAlignment(ReadString(item, "alignment", "Center")),
                        VerticalText = ReadBoolean(item, "vertical_text", height > width * 1.5f),
                        Reviewed = false
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
                    translations = root;
                else if (!TryGetIgnoreCase(root, "translations", out translations) ||
                         translations.ValueKind != JsonValueKind.Array)
                    throw new InvalidDataException("翻译 API 返回的 JSON 中没有 translations 数组。");

                var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in translations.EnumerateArray())
                {
                    var id = ReadString(item, "id", string.Empty);
                    var translation = ReadString(item, "translation", string.Empty).Trim();
                    if (!string.IsNullOrWhiteSpace(id)) result[id] = translation;
                }
                return result;
            }
        }

        private static string ExtractJson(string content)
        {
            var trimmed = (content ?? string.Empty).Trim();
            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                var firstLine = trimmed.IndexOf('\n');
                var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
                if (firstLine >= 0 && lastFence > firstLine)
                    trimmed = trimmed.Substring(firstLine + 1, lastFence - firstLine - 1).Trim();
            }

            var objectStart = trimmed.IndexOf('{');
            var arrayStart = trimmed.IndexOf('[');
            var start = objectStart < 0 ? arrayStart : arrayStart < 0 ? objectStart : Math.Min(objectStart, arrayStart);
            var end = trimmed.LastIndexOf(start >= 0 && trimmed[start] == '[' ? ']' : '}');
            if (start < 0 || end <= start)
                throw new InvalidDataException("AI 响应中未找到有效 JSON。原始响应：\r\n" + Limit(content, 800));
            return trimmed.Substring(start, end - start + 1);
        }

        private static bool TryGetIgnoreCase(JsonElement element, string name, out JsonElement value)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in element.EnumerateObject())
                {
                    if (!string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
                    value = property.Value;
                    return true;
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
            if (!TryGetIgnoreCase(element, name, out var value)) return fallback;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)) return number;
            return value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number)
                ? number
                : fallback;
        }

        private static int ReadArrayInt(JsonElement array, int index)
        {
            var value = array[index];
            return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : 0;
        }

        private static float ReadFloat(JsonElement element, string name, float fallback)
        {
            if (!TryGetIgnoreCase(element, name, out var value)) return fallback;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetSingle(out var number)) return number;
            return value.ValueKind == JsonValueKind.String && float.TryParse(value.GetString(), out number)
                ? number
                : fallback;
        }

        private static bool ReadBoolean(JsonElement element, string name, bool fallback)
        {
            if (!TryGetIgnoreCase(element, name, out var value)) return fallback;
            if (value.ValueKind == JsonValueKind.True) return true;
            if (value.ValueKind == JsonValueKind.False) return false;
            return value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out var parsed)
                ? parsed
                : fallback;
        }

        private static Color ParseColor(string value, Color fallback)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(value) && value.StartsWith("#", StringComparison.Ordinal))
                    return ColorTranslator.FromHtml(value);
                var named = Color.FromName(value ?? string.Empty);
                return named.IsKnownColor || named.IsNamedColor ? named : fallback;
            }
            catch
            {
                return fallback;
            }
        }

        private static string NormalizeAlignment(string value)
        {
            if (string.Equals(value, "Left", StringComparison.OrdinalIgnoreCase)) return "Left";
            if (string.Equals(value, "Right", StringComparison.OrdinalIgnoreCase)) return "Right";
            return "Center";
        }

        private static int Clamp(int value, int min, int max) => Math.Max(min, Math.Min(max, value));

        private static string Limit(string value, int length)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= length) return value ?? string.Empty;
            return value.Substring(0, length) + "...";
        }
    }
}
