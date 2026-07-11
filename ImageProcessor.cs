using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace GalgameUiTranslator
{
    public sealed class RasterImageMetadata
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public bool HasAlpha { get; set; }
        public string FormatName { get; set; } = string.Empty;
        public int MipMapCount { get; set; } = 1;
    }

    public static class ImageProcessor
    {
        private const float MinimumFontSize = 7f;
        private static readonly string ClosingPunctuation = "，。！？；：）》】」』、…—,.!?;:%)]}";
        private static readonly string OpeningPunctuation = "（《【「『“‘([{<";

        public static Bitmap LoadBitmapUnlocked(string path)
        {
            if (Path.GetExtension(path).Equals(".dds", StringComparison.OrdinalIgnoreCase))
                return DdsCodec.Load(path);

            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var source = Image.FromStream(stream, false, false))
            {
                var bitmap = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
                if (source.HorizontalResolution > 0 && source.VerticalResolution > 0)
                {
                    bitmap.SetResolution(source.HorizontalResolution, source.VerticalResolution);
                }

                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.CompositingMode = CompositingMode.SourceCopy;
                    graphics.DrawImage(source, new Rectangle(0, 0, source.Width, source.Height));
                }

                return bitmap;
            }
        }

        public static RasterImageMetadata ReadMetadata(string path)
        {
            if (Path.GetExtension(path).Equals(".dds", StringComparison.OrdinalIgnoreCase))
            {
                var info = DdsCodec.ReadInfo(path);
                return new RasterImageMetadata
                {
                    Width = info.Width,
                    Height = info.Height,
                    HasAlpha = info.HasAlpha,
                    FormatName = info.FormatName,
                    MipMapCount = info.MipMapCount
                };
            }

            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var image = Image.FromStream(stream, false, false))
            {
                return new RasterImageMetadata
                {
                    Width = image.Width,
                    Height = image.Height,
                    HasAlpha = Image.IsAlphaPixelFormat(image.PixelFormat),
                    FormatName = image.RawFormat.ToString(),
                    MipMapCount = 1
                };
            }
        }

        public static Bitmap RenderDocument(string sourcePath, ImageDocument document)
        {
            var bitmap = LoadBitmapUnlocked(sourcePath);
            RenderOnto(bitmap, document);
            return bitmap;
        }

        public static Bitmap RenderPreview(Bitmap source, ImageDocument document)
        {
            var bitmap = source.Clone(
                new Rectangle(0, 0, source.Width, source.Height),
                PixelFormat.Format32bppArgb);
            RenderOnto(bitmap, document);
            return bitmap;
        }

        public static void ExportDocument(string sourcePath, string outputPath, ImageDocument document)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? string.Empty);
            if (document.Regions.Count == 0)
            {
                File.Copy(sourcePath, outputPath, true);
                return;
            }

            using (var bitmap = RenderDocument(sourcePath, document))
            {
                SaveByExtension(bitmap, outputPath, sourcePath);
            }
        }

        public static bool CheckTextFits(TextRegion region, out float fittedFontSize)
        {
            return AdvancedTextRenderer.CheckFits(region, out fittedFontSize);
        }

        private static void RenderOnto(Bitmap bitmap, ImageDocument document)
        {
            foreach (var region in document.Regions)
            {
                ClearRegion(bitmap, region);
            }

            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

                foreach (var region in document.Regions)
                {
                    DrawTranslatedText(graphics, region);
                }
            }
        }

        private static void ClearRegion(Bitmap bitmap, TextRegion region)
        {
            if (string.Equals(region.BackgroundMode, "Keep", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var mask = RepairMaskService.BuildMask(bitmap.Width, bitmap.Height, region);
            var rect = RepairMaskService.GetBounds(mask, bitmap.Width, bitmap.Height);
            if (rect.Width <= 0 || rect.Height <= 0)
            {
                return;
            }

            var data = bitmap.LockBits(
                new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                ImageLockMode.ReadWrite,
                PixelFormat.Format32bppArgb);
            try
            {
                var stride = data.Stride / 4;
                var pixels = new int[Math.Abs(data.Stride) / 4 * bitmap.Height];
                Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
                var original = (int[])pixels.Clone();

                if (string.Equals(region.BackgroundMode, "Transparent", StringComparison.OrdinalIgnoreCase))
                {
                    for (var y = rect.Top; y < rect.Bottom; y++)
                    {
                        for (var x = rect.Left; x < rect.Right; x++)
                        {
                            if (mask[y * bitmap.Width + x])
                                pixels[y * stride + x] = Color.Transparent.ToArgb();
                        }
                    }
                }
                else if (string.Equals(region.BackgroundMode, "Solid", StringComparison.OrdinalIgnoreCase))
                {
                    var sampled = SampleMaskBorderColor(
                        original, mask, stride, bitmap.Width, bitmap.Height, rect);
                    for (var y = rect.Top; y < rect.Bottom; y++)
                    {
                        for (var x = rect.Left; x < rect.Right; x++)
                        {
                            if (mask[y * bitmap.Width + x])
                                pixels[y * stride + x] = sampled;
                        }
                    }
                }
                else if (string.Equals(region.BackgroundMode, "ContentAware", StringComparison.OrdinalIgnoreCase))
                {
                    FillContentAware(original, pixels, mask, stride, bitmap.Width, bitmap.Height, rect);
                }
                else
                {
                    FillGradient(original, pixels, mask, stride, bitmap.Width, bitmap.Height, rect);
                }

                Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }

        private static void FillGradient(
            int[] source,
            int[] target,
            bool[] mask,
            int stride,
            int imageWidth,
            int imageHeight,
            Rectangle rect)
        {
            var leftX = Math.Max(0, rect.Left - 1);
            var rightX = Math.Min(imageWidth - 1, rect.Right);
            var topY = Math.Max(0, rect.Top - 1);
            var bottomY = Math.Min(imageHeight - 1, rect.Bottom);

            for (var y = rect.Top; y < rect.Bottom; y++)
            {
                var verticalRatio = (y - rect.Top + 1f) / (rect.Height + 1f);
                for (var x = rect.Left; x < rect.Right; x++)
                {
                    if (!mask[y * imageWidth + x]) continue;
                    var horizontalRatio = (x - rect.Left + 1f) / (rect.Width + 1f);
                    var left = source[y * stride + leftX];
                    var right = source[y * stride + rightX];
                    var top = source[topY * stride + x];
                    var bottom = source[bottomY * stride + x];
                    var horizontal = LerpColor(left, right, horizontalRatio);
                    var vertical = LerpColor(top, bottom, verticalRatio);
                    target[y * stride + x] = LerpColor(horizontal, vertical, 0.5f);
                }
            }
        }

        private static void FillContentAware(
            int[] source,
            int[] target,
            bool[] mask,
            int stride,
            int imageWidth,
            int imageHeight,
            Rectangle rect)
        {
            var known = new bool[mask.Length];
            var remaining = 0;
            for (var index = 0; index < mask.Length; index++)
            {
                known[index] = !mask[index];
                if (mask[index]) remaining++;
            }

            var stagedColors = new int[mask.Length];
            var staged = new List<int>();
            while (remaining > 0)
            {
                staged.Clear();
                for (var y = rect.Top; y < rect.Bottom; y++)
                {
                    for (var x = rect.Left; x < rect.Right; x++)
                    {
                        var maskIndex = y * imageWidth + x;
                        if (!mask[maskIndex] || known[maskIndex]) continue;
                        if (!TryAverageKnownNeighbors(
                                target, known, stride, imageWidth, imageHeight, x, y, out var color))
                            continue;
                        stagedColors[maskIndex] = color;
                        staged.Add(maskIndex);
                    }
                }

                if (staged.Count == 0)
                {
                    var fallback = SampleMaskBorderColor(
                        source, mask, stride, imageWidth, imageHeight, rect);
                    for (var y = rect.Top; y < rect.Bottom; y++)
                    {
                        for (var x = rect.Left; x < rect.Right; x++)
                        {
                            var maskIndex = y * imageWidth + x;
                            if (mask[maskIndex] && !known[maskIndex])
                            {
                                target[y * stride + x] = fallback;
                                known[maskIndex] = true;
                                remaining--;
                            }
                        }
                    }
                    break;
                }

                foreach (var maskIndex in staged)
                {
                    var y = maskIndex / imageWidth;
                    var x = maskIndex - y * imageWidth;
                    target[y * stride + x] = stagedColors[maskIndex];
                    known[maskIndex] = true;
                    remaining--;
                }
            }

            var filled = (int[])target.Clone();
            for (var y = rect.Top; y < rect.Bottom; y++)
            {
                for (var x = rect.Left; x < rect.Right; x++)
                {
                    if (!mask[y * imageWidth + x]) continue;
                    target[y * stride + x] = AverageArea(
                        filled, stride, imageWidth, imageHeight, x, y, 1);
                }
            }
        }

        private static bool TryAverageKnownNeighbors(
            int[] pixels,
            bool[] known,
            int stride,
            int width,
            int height,
            int x,
            int y,
            out int color)
        {
            long alpha = 0;
            long red = 0;
            long green = 0;
            long blue = 0;
            var count = 0;
            for (var offsetY = -1; offsetY <= 1; offsetY++)
            {
                for (var offsetX = -1; offsetX <= 1; offsetX++)
                {
                    if (offsetX == 0 && offsetY == 0) continue;
                    var sampleX = x + offsetX;
                    var sampleY = y + offsetY;
                    if (sampleX < 0 || sampleY < 0 || sampleX >= width || sampleY >= height) continue;
                    if (!known[sampleY * width + sampleX]) continue;
                    var sample = Color.FromArgb(pixels[sampleY * stride + sampleX]);
                    alpha += sample.A;
                    red += sample.R;
                    green += sample.G;
                    blue += sample.B;
                    count++;
                }
            }

            color = count == 0
                ? 0
                : Color.FromArgb(
                    (int)(alpha / count),
                    (int)(red / count),
                    (int)(green / count),
                    (int)(blue / count)).ToArgb();
            return count > 0;
        }

        private static int AverageArea(
            int[] pixels,
            int stride,
            int width,
            int height,
            int centerX,
            int centerY,
            int radius)
        {
            long alpha = 0;
            long red = 0;
            long green = 0;
            long blue = 0;
            var count = 0;
            for (var y = Math.Max(0, centerY - radius); y <= Math.Min(height - 1, centerY + radius); y++)
            {
                for (var x = Math.Max(0, centerX - radius); x <= Math.Min(width - 1, centerX + radius); x++)
                {
                    var sample = Color.FromArgb(pixels[y * stride + x]);
                    alpha += sample.A;
                    red += sample.R;
                    green += sample.G;
                    blue += sample.B;
                    count++;
                }
            }

            return Color.FromArgb(
                (int)(alpha / count),
                (int)(red / count),
                (int)(green / count),
                (int)(blue / count)).ToArgb();
        }

        private static int SampleMaskBorderColor(
            int[] pixels,
            bool[] mask,
            int stride,
            int width,
            int height,
            Rectangle rect)
        {
            long alpha = 0;
            long red = 0;
            long green = 0;
            long blue = 0;
            var count = 0;
            for (var y = rect.Top; y < rect.Bottom; y++)
            {
                for (var x = rect.Left; x < rect.Right; x++)
                {
                    if (!mask[y * width + x]) continue;
                    for (var direction = 0; direction < 4; direction++)
                    {
                        var sampleX = x + (direction == 0 ? -1 : direction == 1 ? 1 : 0);
                        var sampleY = y + (direction == 2 ? -1 : direction == 3 ? 1 : 0);
                        if (sampleX < 0 || sampleY < 0 || sampleX >= width || sampleY >= height) continue;
                        if (mask[sampleY * width + sampleX]) continue;
                        var sample = Color.FromArgb(pixels[sampleY * stride + sampleX]);
                        alpha += sample.A;
                        red += sample.R;
                        green += sample.G;
                        blue += sample.B;
                        count++;
                    }
                }
            }

            return count == 0
                ? SampleBorderColor(pixels, stride, width, height, rect)
                : Color.FromArgb(
                    (int)(alpha / count),
                    (int)(red / count),
                    (int)(green / count),
                    (int)(blue / count)).ToArgb();
        }

        private static int SampleBorderColor(int[] pixels, int stride, int width, int height, Rectangle rect)
        {
            long a = 0;
            long r = 0;
            long g = 0;
            long b = 0;
            var count = 0;

            void Add(int x, int y)
            {
                if (x < 0 || y < 0 || x >= width || y >= height) return;
                var color = Color.FromArgb(pixels[y * stride + x]);
                a += color.A;
                r += color.R;
                g += color.G;
                b += color.B;
                count++;
            }

            for (var x = rect.Left; x < rect.Right; x++)
            {
                Add(x, rect.Top - 1);
                Add(x, rect.Bottom);
            }

            for (var y = rect.Top; y < rect.Bottom; y++)
            {
                Add(rect.Left - 1, y);
                Add(rect.Right, y);
            }

            return count == 0
                ? Color.Transparent.ToArgb()
                : Color.FromArgb((int)(a / count), (int)(r / count), (int)(g / count), (int)(b / count)).ToArgb();
        }

        private static int LerpColor(int firstArgb, int secondArgb, float amount)
        {
            amount = Math.Max(0f, Math.Min(1f, amount));
            var first = Color.FromArgb(firstArgb);
            var second = Color.FromArgb(secondArgb);
            return Color.FromArgb(
                Lerp(first.A, second.A, amount),
                Lerp(first.R, second.R, amount),
                Lerp(first.G, second.G, amount),
                Lerp(first.B, second.B, amount)).ToArgb();
        }

        private static int Lerp(int first, int second, float amount)
        {
            return (int)Math.Round(first + (second - first) * amount);
        }

        private static void DrawTranslatedText(Graphics graphics, TextRegion region)
        {
            AdvancedTextRenderer.Draw(graphics, region);
        }

        private static string FitText(
            Graphics graphics,
            string text,
            FontFamily family,
            FontStyle style,
            Rectangle bounds,
            ref float fontSize)
        {
            string wrapped = text;
            for (var size = fontSize; size >= MinimumFontSize; size -= 0.5f)
            {
                wrapped = WrapText(graphics, text, family, style, size, bounds.Width);
                using (var font = new Font(family, size, style, GraphicsUnit.Pixel))
                using (var format = StringFormat.GenericTypographic.Clone() as StringFormat)
                {
                    var measured = graphics.MeasureString(wrapped, font, bounds.Width + 1, format);
                    if (measured.Width <= bounds.Width + 1 && measured.Height <= bounds.Height + 1)
                    {
                        fontSize = size;
                        return wrapped;
                    }
                }
            }

            fontSize = MinimumFontSize;
            return WrapText(graphics, text, family, style, fontSize, bounds.Width);
        }

        private static string WrapText(
            Graphics graphics,
            string text,
            FontFamily family,
            FontStyle style,
            float fontSize,
            int maximumWidth)
        {
            if (maximumWidth <= 1)
            {
                return text;
            }

            using (var font = new Font(family, fontSize, style, GraphicsUnit.Pixel))
            using (var format = StringFormat.GenericTypographic.Clone() as StringFormat)
            {
                var output = new List<string>();
                foreach (var paragraph in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
                {
                    if (paragraph.Length == 0)
                    {
                        output.Add(string.Empty);
                        continue;
                    }

                    var current = new StringBuilder();
                    foreach (var element in EnumerateTextElements(paragraph))
                    {
                        var candidate = current + element;
                        var width = graphics.MeasureString(candidate, font, int.MaxValue, format).Width;
                        if (current.Length > 0 && width > maximumWidth && !IsClosing(element))
                        {
                            if (EndsWithOpening(current))
                            {
                                var opening = current[current.Length - 1].ToString();
                                current.Length--;
                                output.Add(current.ToString().TrimEnd());
                                current.Clear();
                                current.Append(opening);
                                current.Append(element);
                            }
                            else
                            {
                                output.Add(current.ToString().TrimEnd());
                                current.Clear();
                                current.Append(element.TrimStart());
                            }
                        }
                        else
                        {
                            current.Append(element);
                        }
                    }

                    output.Add(current.ToString());
                }

                return string.Join("\n", output);
            }
        }

        private static IEnumerable<string> EnumerateTextElements(string text)
        {
            var enumerator = StringInfo.GetTextElementEnumerator(text);
            while (enumerator.MoveNext())
            {
                yield return enumerator.GetTextElement();
            }
        }

        private static bool IsClosing(string element)
        {
            return element.Length > 0 && ClosingPunctuation.IndexOf(element[0]) >= 0;
        }

        private static bool EndsWithOpening(StringBuilder builder)
        {
            return builder.Length > 0 && OpeningPunctuation.IndexOf(builder[builder.Length - 1]) >= 0;
        }

        private static StringFormat CreateStringFormat(TextRegion region)
        {
            var format = new StringFormat(StringFormat.GenericTypographic)
            {
                FormatFlags = StringFormatFlags.NoWrap,
                Trimming = StringTrimming.None
            };

            format.Alignment = region.HorizontalAlignment.Equals("Left", StringComparison.OrdinalIgnoreCase)
                ? StringAlignment.Near
                : region.HorizontalAlignment.Equals("Right", StringComparison.OrdinalIgnoreCase)
                    ? StringAlignment.Far
                    : StringAlignment.Center;
            format.LineAlignment = region.VerticalAlignment.Equals("Top", StringComparison.OrdinalIgnoreCase)
                ? StringAlignment.Near
                : region.VerticalAlignment.Equals("Bottom", StringComparison.OrdinalIgnoreCase)
                    ? StringAlignment.Far
                    : StringAlignment.Center;
            return format;
        }

        private static void SaveByExtension(Bitmap bitmap, string path, string sourcePath)
        {
            var extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension == ".dds")
            {
                DdsCodec.Save(bitmap, path, DdsCodec.ReadInfo(sourcePath));
                return;
            }
            if (extension == ".jpg" || extension == ".jpeg")
            {
                var encoder = ImageCodecInfo.GetImageEncoders()
                    .FirstOrDefault(codec => codec.FormatID == ImageFormat.Jpeg.Guid);
                if (encoder != null)
                {
                    using (var parameters = new EncoderParameters(1))
                    {
                        parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 95L);
                        bitmap.Save(path, encoder, parameters);
                        return;
                    }
                }
            }

            if (extension == ".bmp")
            {
                bitmap.Save(path, ImageFormat.Bmp);
            }
            else
            {
                bitmap.Save(path, ImageFormat.Png);
            }
        }
    }
}
