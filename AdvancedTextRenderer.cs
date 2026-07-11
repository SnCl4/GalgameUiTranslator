using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;

namespace GalgameUiTranslator
{
    public static class AdvancedTextRenderer
    {
        private const float MinimumFontSize = 7f;

        public static bool CheckFits(TextRegion region, out float fittedFontSize)
        {
            fittedFontSize = Math.Max(MinimumFontSize, region.FontSize);
            var text = (region.Translation ?? string.Empty).Trim();
            if (text.Length == 0 || region.Width <= 0 || region.Height <= 0)
                return text.Length == 0;

            using (var bitmap = new Bitmap(1, 1))
            using (var graphics = Graphics.FromImage(bitmap))
            using (var family = FontManager.ResolveFamily(region.FontFamily))
            using (var path = CreateBestPath(graphics, region, family, out fittedFontSize))
                return PathFits(path, region);
        }

        public static void Draw(Graphics graphics, TextRegion region)
        {
            var text = (region.Translation ?? string.Empty).Trim();
            if (text.Length == 0 || region.Width <= 0 || region.Height <= 0) return;

            using (var family = FontManager.ResolveFamily(region.FontFamily))
            using (var path = CreateBestPath(graphics, region, family, out _))
            {
                if (path.PointCount == 0) return;

                if (region.ShadowEnabled)
                {
                    using (var shadow = (GraphicsPath)path.Clone())
                    using (var transform = new Matrix())
                    using (var brush = new SolidBrush(Color.FromArgb(region.ShadowColorArgb)))
                    {
                        transform.Translate(region.ShadowOffsetX, region.ShadowOffsetY);
                        shadow.Transform(transform);
                        graphics.FillPath(brush, shadow);
                    }
                }

                if (region.GlowWidth > 0f)
                {
                    using (var pen = new Pen(
                               Color.FromArgb(region.GlowColorArgb),
                               Math.Max(1f, (region.OutlineWidth + region.GlowWidth) * 2f)))
                    {
                        pen.LineJoin = LineJoin.Round;
                        graphics.DrawPath(pen, path);
                    }
                }

                if (region.OutlineWidth > 0f)
                {
                    using (var pen = new Pen(Color.FromArgb(region.OutlineColorArgb), region.OutlineWidth * 2f))
                    {
                        pen.LineJoin = LineJoin.Round;
                        graphics.DrawPath(pen, path);
                    }
                }

                if (string.Equals(region.TextFillMode, "VerticalGradient", StringComparison.OrdinalIgnoreCase))
                {
                    using (var brush = new LinearGradientBrush(
                               region.Bounds,
                               Color.FromArgb(region.TextColorArgb),
                               Color.FromArgb(region.GradientEndColorArgb),
                               LinearGradientMode.Vertical))
                        graphics.FillPath(brush, path);
                }
                else
                {
                    using (var brush = new SolidBrush(Color.FromArgb(region.TextColorArgb)))
                        graphics.FillPath(brush, path);
                }
            }
        }

        private static GraphicsPath CreateBestPath(
            Graphics graphics,
            TextRegion region,
            FontFamily family,
            out float fittedFontSize)
        {
            var start = Math.Max(MinimumFontSize, region.FontSize);
            if (!region.AutoFit)
            {
                fittedFontSize = start;
                return CreatePath(graphics, region, family, start);
            }

            GraphicsPath last = null;
            for (var size = start; size >= MinimumFontSize; size -= 0.5f)
            {
                last?.Dispose();
                last = CreatePath(graphics, region, family, size);
                if (PathFits(last, region))
                {
                    fittedFontSize = size;
                    return last;
                }
            }

            fittedFontSize = MinimumFontSize;
            return last ?? CreatePath(graphics, region, family, MinimumFontSize);
        }

        private static GraphicsPath CreatePath(
            Graphics graphics,
            TextRegion region,
            FontFamily family,
            float fontSize)
        {
            var style = region.Bold ? FontStyle.Bold : FontStyle.Regular;
            var path = region.VerticalText
                ? CreateVerticalPath(graphics, region, family, style, fontSize)
                : CreateHorizontalPath(graphics, region, family, style, fontSize);

            if (Math.Abs(region.RotationDegrees) > 0.01f && path.PointCount > 0)
            {
                using (var transform = new Matrix())
                {
                    transform.RotateAt(
                        region.RotationDegrees,
                        new PointF(region.X + region.Width / 2f, region.Y + region.Height / 2f));
                    path.Transform(transform);
                }
            }
            return path;
        }

        private static GraphicsPath CreateHorizontalPath(
            Graphics graphics,
            TextRegion region,
            FontFamily family,
            FontStyle style,
            float fontSize)
        {
            var path = new GraphicsPath();
            using (var font = new Font(family, fontSize, style, GraphicsUnit.Pixel))
            using (var format = CreateTypographicFormat())
            {
                var lines = WrapHorizontal(
                    graphics,
                    font,
                    format,
                    region.Translation ?? string.Empty,
                    region.Width,
                    region.LetterSpacing);
                var lineHeight = Math.Max(1f, font.GetHeight(graphics) * Math.Max(0.5f, region.LineSpacing));
                var totalHeight = lines.Count * lineHeight;
                var y = AlignStart(region.Y, region.Height, totalHeight, region.VerticalAlignment);
                foreach (var line in lines)
                {
                    var elements = EnumerateTextElements(line).ToList();
                    var widths = elements.Select(element => MeasureElement(graphics, font, format, element)).ToList();
                    var lineWidth = widths.Sum() + Math.Max(0, elements.Count - 1) * region.LetterSpacing;
                    var x = AlignStart(region.X, region.Width, lineWidth, region.HorizontalAlignment);
                    for (var index = 0; index < elements.Count; index++)
                    {
                        path.AddString(elements[index], family, (int)style, fontSize, new PointF(x, y), format);
                        x += widths[index] + region.LetterSpacing;
                    }
                    y += lineHeight;
                }
            }
            return path;
        }

        private static GraphicsPath CreateVerticalPath(
            Graphics graphics,
            TextRegion region,
            FontFamily family,
            FontStyle style,
            float fontSize)
        {
            var path = new GraphicsPath();
            using (var font = new Font(family, fontSize, style, GraphicsUnit.Pixel))
            using (var format = CreateTypographicFormat())
            {
                var cellHeight = Math.Max(1f,
                    font.GetHeight(graphics) * Math.Max(0.5f, region.LineSpacing) + region.LetterSpacing);
                var columnWidth = Math.Max(1f, fontSize * Math.Max(0.75f, region.LineSpacing));
                var capacity = Math.Max(1, (int)Math.Floor(region.Height / cellHeight));
                var columns = new List<List<string>>();
                foreach (var paragraph in NormalizeLines(region.Translation ?? string.Empty))
                {
                    var elements = EnumerateTextElements(paragraph).ToList();
                    if (elements.Count == 0)
                    {
                        columns.Add(new List<string>());
                        continue;
                    }
                    for (var index = 0; index < elements.Count; index += capacity)
                        columns.Add(elements.Skip(index).Take(capacity).ToList());
                }
                if (columns.Count == 0) columns.Add(new List<string>());

                var totalWidth = columns.Count * columnWidth;
                var left = AlignStart(region.X, region.Width, totalWidth, region.HorizontalAlignment);
                for (var columnIndex = 0; columnIndex < columns.Count; columnIndex++)
                {
                    var column = columns[columnIndex];
                    var columnHeight = column.Count * cellHeight;
                    var y = AlignStart(region.Y, region.Height, columnHeight, region.VerticalAlignment);
                    var x = left + (columns.Count - columnIndex - 1) * columnWidth;
                    foreach (var element in column)
                    {
                        var glyphWidth = MeasureElement(graphics, font, format, element);
                        path.AddString(
                            element,
                            family,
                            (int)style,
                            fontSize,
                            new PointF(x + Math.Max(0f, (columnWidth - glyphWidth) / 2f), y),
                            format);
                        y += cellHeight;
                    }
                }
            }
            return path;
        }

        private static List<string> WrapHorizontal(
            Graphics graphics,
            Font font,
            StringFormat format,
            string text,
            float maximumWidth,
            float letterSpacing)
        {
            var output = new List<string>();
            foreach (var paragraph in NormalizeLines(text))
            {
                if (paragraph.Length == 0)
                {
                    output.Add(string.Empty);
                    continue;
                }

                var current = new List<string>();
                var currentWidth = 0f;
                foreach (var element in EnumerateTextElements(paragraph))
                {
                    var elementWidth = MeasureElement(graphics, font, format, element);
                    var candidate = currentWidth + (current.Count > 0 ? letterSpacing : 0f) + elementWidth;
                    if (current.Count > 0 && candidate > maximumWidth)
                    {
                        output.Add(string.Concat(current));
                        current.Clear();
                        currentWidth = 0f;
                    }
                    if (current.Count > 0) currentWidth += letterSpacing;
                    current.Add(element);
                    currentWidth += elementWidth;
                }
                output.Add(string.Concat(current));
            }
            return output;
        }

        private static IEnumerable<string> NormalizeLines(string text)
        {
            return text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        }

        private static IEnumerable<string> EnumerateTextElements(string text)
        {
            var enumerator = StringInfo.GetTextElementEnumerator(text);
            while (enumerator.MoveNext()) yield return enumerator.GetTextElement();
        }

        private static float MeasureElement(Graphics graphics, Font font, StringFormat format, string element)
        {
            return Math.Max(1f, graphics.MeasureString(element, font, int.MaxValue, format).Width);
        }

        private static float AlignStart(float origin, float available, float content, string alignment)
        {
            if (string.Equals(alignment, "Left", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(alignment, "Top", StringComparison.OrdinalIgnoreCase))
                return origin;
            if (string.Equals(alignment, "Right", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(alignment, "Bottom", StringComparison.OrdinalIgnoreCase))
                return origin + available - content;
            return origin + (available - content) / 2f;
        }

        private static StringFormat CreateTypographicFormat()
        {
            return new StringFormat(StringFormat.GenericTypographic)
            {
                FormatFlags = StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoClip,
                Trimming = StringTrimming.None
            };
        }

        private static bool PathFits(GraphicsPath path, TextRegion region)
        {
            if (path == null || path.PointCount == 0) return true;
            var bounds = path.GetBounds();
            var effect = Math.Max(region.OutlineWidth, region.OutlineWidth + region.GlowWidth);
            bounds.Inflate(effect, effect);
            if (region.ShadowEnabled)
            {
                bounds = RectangleF.Union(bounds, new RectangleF(
                    bounds.X + region.ShadowOffsetX,
                    bounds.Y + region.ShadowOffsetY,
                    bounds.Width,
                    bounds.Height));
            }
            const float tolerance = 1.5f;
            return bounds.Left >= region.X - tolerance &&
                   bounds.Top >= region.Y - tolerance &&
                   bounds.Right <= region.X + region.Width + tolerance &&
                   bounds.Bottom <= region.Y + region.Height + tolerance;
        }
    }
}
