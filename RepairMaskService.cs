using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace GalgameUiTranslator
{
    public static class RepairMaskService
    {
        public static bool[] BuildMask(int width, int height, TextRegion region)
        {
            if (width <= 0 || height <= 0)
                return Array.Empty<bool>();

            var mask = new bool[width * height];
            var strokes = region?.RepairMaskStrokes;
            if (strokes != null && strokes.Count > 0)
            {
                foreach (var stroke in strokes)
                    RasterizeStroke(mask, width, height, stroke);

                if (region.ClearPadding > 0)
                    mask = Dilate(mask, width, height, region.ClearPadding);
                return mask;
            }

            if (region == null) return mask;
            var padding = Math.Max(0, region.ClearPadding);
            var rect = Rectangle.Intersect(
                new Rectangle(0, 0, width, height),
                Rectangle.Inflate(region.Bounds, padding, padding));
            for (var y = rect.Top; y < rect.Bottom; y++)
            {
                var offset = y * width;
                for (var x = rect.Left; x < rect.Right; x++)
                    mask[offset + x] = true;
            }

            return mask;
        }

        public static Rectangle GetBounds(bool[] mask, int width, int height)
        {
            if (mask == null || mask.Length < width * height || width <= 0 || height <= 0)
                return Rectangle.Empty;

            var left = width;
            var top = height;
            var right = -1;
            var bottom = -1;
            for (var y = 0; y < height; y++)
            {
                var offset = y * width;
                for (var x = 0; x < width; x++)
                {
                    if (!mask[offset + x]) continue;
                    left = Math.Min(left, x);
                    top = Math.Min(top, y);
                    right = Math.Max(right, x);
                    bottom = Math.Max(bottom, y);
                }
            }

            return right < left || bottom < top
                ? Rectangle.Empty
                : Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
        }

        private static void RasterizeStroke(bool[] mask, int width, int height, RepairMaskStroke stroke)
        {
            if (stroke?.Points == null || stroke.Points.Count == 0) return;
            var value = !stroke.Eraser;
            var radius = Math.Max(1, stroke.Diameter) / 2f;
            var previous = stroke.Points[0];
            PaintDisk(mask, width, height, previous.X, previous.Y, radius, value);
            for (var index = 1; index < stroke.Points.Count; index++)
            {
                var current = stroke.Points[index];
                var deltaX = current.X - previous.X;
                var deltaY = current.Y - previous.Y;
                var steps = Math.Max(1, Math.Max(Math.Abs(deltaX), Math.Abs(deltaY)));
                for (var step = 1; step <= steps; step++)
                {
                    var amount = step / (float)steps;
                    var x = (int)Math.Round(previous.X + deltaX * amount);
                    var y = (int)Math.Round(previous.Y + deltaY * amount);
                    PaintDisk(mask, width, height, x, y, radius, value);
                }
                previous = current;
            }
        }

        private static void PaintDisk(
            bool[] mask,
            int width,
            int height,
            int centerX,
            int centerY,
            float radius,
            bool value)
        {
            var minimumX = Math.Max(0, (int)Math.Floor(centerX - radius));
            var maximumX = Math.Min(width - 1, (int)Math.Ceiling(centerX + radius));
            var minimumY = Math.Max(0, (int)Math.Floor(centerY - radius));
            var maximumY = Math.Min(height - 1, (int)Math.Ceiling(centerY + radius));
            var radiusSquared = radius * radius;
            for (var y = minimumY; y <= maximumY; y++)
            {
                var offset = y * width;
                for (var x = minimumX; x <= maximumX; x++)
                {
                    var deltaX = x - centerX;
                    var deltaY = y - centerY;
                    if (deltaX * deltaX + deltaY * deltaY <= radiusSquared)
                        mask[offset + x] = value;
                }
            }
        }

        private static bool[] Dilate(bool[] source, int width, int height, int radius)
        {
            radius = Math.Max(0, Math.Min(30, radius));
            if (radius == 0) return source;
            var horizontal = new bool[source.Length];
            for (var y = 0; y < height; y++)
            {
                var count = 0;
                for (var x = -radius; x < width + radius; x++)
                {
                    var entering = x + radius;
                    var leaving = x - radius - 1;
                    if (entering >= 0 && entering < width && source[y * width + entering]) count++;
                    if (leaving >= 0 && leaving < width && source[y * width + leaving]) count--;
                    if (x >= 0 && x < width) horizontal[y * width + x] = count > 0;
                }
            }

            var output = new bool[source.Length];
            for (var x = 0; x < width; x++)
            {
                var count = 0;
                for (var y = -radius; y < height + radius; y++)
                {
                    var entering = y + radius;
                    var leaving = y - radius - 1;
                    if (entering >= 0 && entering < height && horizontal[entering * width + x]) count++;
                    if (leaving >= 0 && leaving < height && horizontal[leaving * width + x]) count--;
                    if (y >= 0 && y < height) output[y * width + x] = count > 0;
                }
            }

            return output;
        }
    }
}
