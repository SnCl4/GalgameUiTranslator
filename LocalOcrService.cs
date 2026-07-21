using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RapidOcrNet;
using SkiaSharp;

namespace GalgameUiTranslator
{
    public sealed class LocalOcrService : IDisposable
    {
        private readonly object _sync = new object();
        private RapidOcr _ocr;

        public bool IsAvailable => GetMissingModelFiles().Count == 0;

        public IReadOnlyList<string> GetMissingModelFiles()
        {
            return GetRequiredModelPaths().Where(path => !File.Exists(path)).ToArray();
        }

        public Task<ApiAnalysisResult> AnalyzeAsync(
            string imagePath,
            int imageWidth,
            int imageHeight,
            AppSettings settings,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(imagePath))
                throw new ArgumentException("图片路径不能为空。", nameof(imagePath));
            if (!File.Exists(imagePath))
                throw new FileNotFoundException("待识别图片不存在。", imagePath);
            if (!IsAvailable)
            {
                throw new InvalidOperationException(
                    "本地 OCR 模型不完整：" + string.Join("、", GetMissingModelFiles().Select(Path.GetFileName)));
            }

            return Task.Run(
                () => AnalyzeCore(imagePath, imageWidth, imageHeight, settings, cancellationToken),
                cancellationToken);
        }

        private ApiAnalysisResult AnalyzeCore(
            string imagePath,
            int imageWidth,
            int imageHeight,
            AppSettings settings,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OcrResult result;
            lock (_sync)
            {
                EnsureInitialized();
                var options = RapidOcrOptions.PPOCRv6 with
                {
                    TextScore = Math.Max(0.1f, Math.Min(0.95f, settings.LocalOcrMinimumConfidence)),
                    ReturnWordBox = false,
                    DoAngle = true
                };
                result = DetectImage(imagePath, options);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var regions = new List<TextRegion>();
            foreach (var block in result.TextBlocks ?? Array.Empty<TextBlock>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var text = NormalizeText(block.Text);
                if (!ContainsJapaneseText(text) || block.BoxPoints == null || block.BoxPoints.Length < 4)
                    continue;

                var confidence = block.CharScores != null && block.CharScores.Any()
                    ? block.CharScores.Average()
                    : settings.LocalOcrMinimumConfidence;
                var left = block.BoxPoints.Min(point => point.X);
                var top = block.BoxPoints.Min(point => point.Y);
                var right = block.BoxPoints.Max(point => point.X);
                var bottom = block.BoxPoints.Max(point => point.Y);
                var x = Clamp((int)Math.Floor((double)left) - 2, 0, Math.Max(0, imageWidth - 1));
                var y = Clamp((int)Math.Floor((double)top) - 2, 0, Math.Max(0, imageHeight - 1));
                var x2 = Clamp((int)Math.Ceiling((double)right) + 2, x + 1, imageWidth);
                var y2 = Clamp((int)Math.Ceiling((double)bottom) + 2, y + 1, imageHeight);
                var width = Math.Max(1, x2 - x);
                var height = Math.Max(1, y2 - y);

                regions.Add(new TextRegion
                {
                    X = x,
                    Y = y,
                    Width = width,
                    Height = height,
                    SourceText = text,
                    Confidence = Math.Max(0f, Math.Min(1f, confidence)),
                    FontSize = Math.Max(7f, Math.Min(72f, height * 0.7f)),
                    VerticalText = height > width * 1.35f && text.Length > 1,
                    HorizontalAlignment = "Center",
                    VerticalAlignment = "Center"
                });
            }

            return new ApiAnalysisResult
            {
                Regions = regions,
                RawResponse = result.StrRes ?? string.Empty,
                ProviderName = "本地 OCR"
            };
        }

        private void EnsureInitialized()
        {
            if (_ocr != null) return;
            var paths = GetRequiredModelPaths();
            var ocr = new RapidOcr();
            try
            {
                ocr.InitModels(paths[0], paths[1], paths[2], paths[3]);
                _ocr = ocr;
            }
            catch
            {
                ocr.Dispose();
                throw;
            }
        }

        private OcrResult DetectImage(string imagePath, RapidOcrOptions options)
        {
            if (!Path.GetExtension(imagePath).Equals(".dds", StringComparison.OrdinalIgnoreCase))
                return _ocr.Detect(imagePath, options);

            using (var bitmap = ImageProcessor.LoadBitmapUnlocked(imagePath))
            using (var stream = new MemoryStream())
            {
                bitmap.Save(stream, ImageFormat.Png);
                stream.Position = 0;
                using (var skBitmap = SKBitmap.Decode(stream))
                {
                    if (skBitmap == null)
                        throw new InvalidDataException("无法把 DDS 转换为 OCR 可识别的图像。");
                    return _ocr.Detect(skBitmap, options);
                }
            }
        }

        private static IReadOnlyList<string> GetRequiredModelPaths()
        {
            var baseDirectory = AppContext.BaseDirectory;
            return new[]
            {
                Path.Combine(baseDirectory, "models", "v6", "PP-OCRv6_det_small.onnx"),
                Path.Combine(baseDirectory, "models", "v5", "ch_PP-LCNet_x0_25_textline_ori_cls_mobile.onnx"),
                Path.Combine(baseDirectory, "models", "v6", "PP-OCRv6_rec_small.onnx"),
                Path.Combine(baseDirectory, "models", "v6", "ppocrv6_small_dict.txt")
            };
        }

        public static bool ContainsJapaneseText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            foreach (var character in text)
            {
                if ((character >= '\u3040' && character <= '\u30ff') ||
                    (character >= '\u31f0' && character <= '\u31ff') ||
                    (character >= '\u3400' && character <= '\u9fff') ||
                    (character >= '\uff66' && character <= '\uff9f'))
                {
                    return true;
                }
            }

            return false;
        }

        internal static string NormalizeText(string text)
        {
            return string.Join(" ", (text ?? string.Empty)
                    .Replace('\r', '\n')
                    .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(part => part.Trim()))
                .Trim();
        }

        private static int Clamp(int value, int minimum, int maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }

        public void Dispose()
        {
            lock (_sync)
            {
                _ocr?.Dispose();
                _ocr = null;
            }
        }
    }
}
