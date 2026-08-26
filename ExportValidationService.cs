using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace GalgameUiTranslator
{
    public static class ExportValidationService
    {
        public static string Validate(string sourcePath, string outputPath, ImageDocument document)
        {
            var errors = new List<string>();
            if (document == null) return "缺少导出图片的工程记录。";
            if (!File.Exists(outputPath)) return "导出文件没有生成。";
            if (new FileInfo(outputPath).Length == 0) return "导出文件为空。";

            try
            {
                var sourceMetadata = ImageProcessor.ReadMetadata(sourcePath);
                var outputMetadata = ImageProcessor.ReadMetadata(outputPath);
                if (outputMetadata.Width != document.Width || outputMetadata.Height != document.Height)
                {
                    errors.Add(
                        $"导出尺寸异常：期望 {document.Width}×{document.Height}，" +
                        $"实际 {outputMetadata.Width}×{outputMetadata.Height}。");
                }
                if (sourceMetadata.HasAlpha && !outputMetadata.HasAlpha)
                    errors.Add("源图片包含 Alpha 通道，但导出图片不再包含 Alpha 通道。");
                if (Path.GetExtension(sourcePath).Equals(".dds", StringComparison.OrdinalIgnoreCase) &&
                    (!string.Equals(sourceMetadata.FormatName, outputMetadata.FormatName, StringComparison.OrdinalIgnoreCase) ||
                     sourceMetadata.MipMapCount != outputMetadata.MipMapCount))
                {
                    errors.Add(
                        $"DDS 格式参数发生变化：源文件 {sourceMetadata.FormatName}/{sourceMetadata.MipMapCount} 级 mipmap，" +
                        $"导出文件 {outputMetadata.FormatName}/{outputMetadata.MipMapCount} 级。");
                }
                if (errors.Count > 0) return string.Join("\r\n", errors);

                if ((document.Regions ?? new List<TextRegion>()).Count == 0)
                {
                    if (!FilesEqual(sourcePath, outputPath))
                        errors.Add("图片没有汉化区域，但导出文件与源文件并不完全相同。");
                    return string.Join("\r\n", errors);
                }

                using (var source = ImageProcessor.LoadBitmapUnlocked(sourcePath))
                using (var expected = ImageProcessor.RenderDocument(sourcePath, document))
                using (var output = ImageProcessor.LoadBitmapUnlocked(outputPath))
                {
                    var sourcePixels = CopyPixels(source);
                    var expectedPixels = CopyPixels(expected);
                    var outputPixels = CopyPixels(output);
                    var extension = Path.GetExtension(outputPath).ToLowerInvariant();
                    var lossless = extension == ".png" || extension == ".bmp";
                    ValidateRenderedPixels(expectedPixels, outputPixels, lossless, errors);
                    ValidateVisibleRegionChanges(
                        sourcePixels,
                        outputPixels,
                        document,
                        lossless,
                        errors);
                }
            }
            catch (Exception exception)
            {
                errors.Add("无法验证导出文件：" + exception.Message);
            }

            return string.Join("\r\n", errors.Distinct(StringComparer.Ordinal));
        }

        private static void ValidateRenderedPixels(
            PixelBuffer expected,
            PixelBuffer actual,
            bool lossless,
            ICollection<string> errors)
        {
            if (expected.Width != actual.Width || expected.Height != actual.Height) return;
            long totalDifference = 0;
            var comparedChannels = 0L;
            var mismatchedPixels = 0;
            var severePixels = 0;
            for (var index = 0; index < expected.Pixels.Length; index++)
            {
                var left = Color.FromArgb(expected.Pixels[index]);
                var right = Color.FromArgb(actual.Pixels[index]);
                var alphaDifference = Math.Abs(left.A - right.A);
                var colorDifference = left.A == 0 && right.A == 0
                    ? 0
                    : Math.Abs(left.R - right.R) + Math.Abs(left.G - right.G) + Math.Abs(left.B - right.B);
                var difference = alphaDifference + colorDifference;
                if (difference > 0) mismatchedPixels++;
                if (difference > 100) severePixels++;
                totalDifference += difference;
                comparedChannels += 4;
            }

            if (lossless && mismatchedPixels > 0)
            {
                errors.Add($"无损导出像素验收失败：有 {mismatchedPixels} 个像素与预期渲染结果不同。");
                return;
            }

            if (!lossless)
            {
                var averageChannelDifference = totalDifference / (double)Math.Max(1, comparedChannels);
                var severeRatio = severePixels / (double)Math.Max(1, expected.Pixels.Length);
                if (averageChannelDifference > 20d || severeRatio > 0.08d)
                {
                    errors.Add(
                        $"有损导出视觉偏差过大：平均通道差 {averageChannelDifference:0.0}，" +
                        $"严重偏差像素 {severeRatio:P1}。");
                }
            }
        }

        private static void ValidateVisibleRegionChanges(
            PixelBuffer source,
            PixelBuffer output,
            ImageDocument document,
            bool lossless,
            ICollection<string> errors)
        {
            var threshold = lossless ? 0 : 30;
            for (var index = 0; index < document.Regions.Count; index++)
            {
                var region = document.Regions[index];
                if (region == null || string.IsNullOrWhiteSpace(region.Translation)) continue;
                var bounds = Rectangle.Intersect(
                    new Rectangle(0, 0, source.Width, source.Height),
                    Rectangle.Inflate(region.Bounds, Math.Max(2, region.ClearPadding), Math.Max(2, region.ClearPadding)));
                if (bounds.Width <= 0 || bounds.Height <= 0) continue;

                var changed = 0;
                for (var y = bounds.Top; y < bounds.Bottom; y++)
                for (var x = bounds.Left; x < bounds.Right; x++)
                {
                    var pixelIndex = y * source.Width + x;
                    if (PixelDifference(source.Pixels[pixelIndex], output.Pixels[pixelIndex]) > threshold)
                        changed++;
                }

                var minimumChanged = Math.Max(3, bounds.Width * bounds.Height / 2000);
                if (changed < minimumChanged)
                    errors.Add($"第 {index + 1} 个汉化区域在导出图中没有产生足够的可见变化。");
            }
        }

        private static int PixelDifference(int leftArgb, int rightArgb)
        {
            var left = Color.FromArgb(leftArgb);
            var right = Color.FromArgb(rightArgb);
            if (left.A == 0 && right.A == 0) return 0;
            return Math.Abs(left.A - right.A) + Math.Abs(left.R - right.R) +
                   Math.Abs(left.G - right.G) + Math.Abs(left.B - right.B);
        }

        private static PixelBuffer CopyPixels(Bitmap source)
        {
            using (var bitmap = source.PixelFormat == PixelFormat.Format32bppArgb
                       ? (Bitmap)source.Clone()
                       : source.Clone(new Rectangle(0, 0, source.Width, source.Height), PixelFormat.Format32bppArgb))
            {
                var pixels = new int[bitmap.Width * bitmap.Height];
                var data = bitmap.LockBits(
                    new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                    ImageLockMode.ReadOnly,
                    PixelFormat.Format32bppArgb);
                try
                {
                    for (var y = 0; y < bitmap.Height; y++)
                    {
                        var row = IntPtr.Add(data.Scan0, y * data.Stride);
                        Marshal.Copy(row, pixels, y * bitmap.Width, bitmap.Width);
                    }
                }
                finally
                {
                    bitmap.UnlockBits(data);
                }
                return new PixelBuffer(bitmap.Width, bitmap.Height, pixels);
            }
        }

        private static bool FilesEqual(string leftPath, string rightPath)
        {
            var leftInfo = new FileInfo(leftPath);
            var rightInfo = new FileInfo(rightPath);
            if (leftInfo.Length != rightInfo.Length) return false;
            var leftBuffer = new byte[64 * 1024];
            var rightBuffer = new byte[leftBuffer.Length];
            using (var left = new FileStream(leftPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var right = new FileStream(rightPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                while (true)
                {
                    var leftRead = left.Read(leftBuffer, 0, leftBuffer.Length);
                    var rightRead = right.Read(rightBuffer, 0, rightBuffer.Length);
                    if (leftRead != rightRead) return false;
                    if (leftRead == 0) return true;
                    for (var index = 0; index < leftRead; index++)
                    {
                        if (leftBuffer[index] != rightBuffer[index]) return false;
                    }
                }
            }
        }

        private sealed class PixelBuffer
        {
            public PixelBuffer(int width, int height, int[] pixels)
            {
                Width = width;
                Height = height;
                Pixels = pixels;
            }

            public int Width { get; }
            public int Height { get; }
            public int[] Pixels { get; }
        }
    }
}
