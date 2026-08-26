using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;

namespace GalgameUiTranslator
{
    public static class TextRegionMergeService
    {
        public static List<TextRegion> MergeLocalAndCloud(
            IEnumerable<TextRegion> localRegions,
            IEnumerable<TextRegion> cloudRegions,
            int imageWidth,
            int imageHeight)
        {
            var result = Clean(localRegions, imageWidth, imageHeight);
            foreach (var cloud in Clean(cloudRegions, imageWidth, imageHeight))
            {
                var duplicate = result
                    .Select((region, index) => new { Region = region, Index = index, Score = MatchScore(region, cloud) })
                    .Where(item => item.Score >= 0.55f)
                    .OrderByDescending(item => item.Score)
                    .FirstOrDefault();
                if (duplicate == null)
                {
                    result.Add(cloud);
                    continue;
                }

                result[duplicate.Index] = MergePair(duplicate.Region, cloud);
            }

            return result
                .OrderBy(region => region.Y)
                .ThenBy(region => region.X)
                .ToList();
        }

        public static List<TextRegion> MergeCloudTiles(
            IEnumerable<TextRegion> existing,
            IEnumerable<TextRegion> incoming,
            int imageWidth,
            int imageHeight)
        {
            return MergeLocalAndCloud(existing, incoming, imageWidth, imageHeight);
        }

        private static List<TextRegion> Clean(
            IEnumerable<TextRegion> regions,
            int imageWidth,
            int imageHeight)
        {
            var result = new List<TextRegion>();
            foreach (var source in regions ?? Enumerable.Empty<TextRegion>())
            {
                if (source == null || string.IsNullOrWhiteSpace(source.SourceText)) continue;
                var region = Clone(source);
                region.X = Math.Max(0, Math.Min(Math.Max(0, imageWidth - 1), region.X));
                region.Y = Math.Max(0, Math.Min(Math.Max(0, imageHeight - 1), region.Y));
                region.Width = Math.Max(1, Math.Min(Math.Max(1, imageWidth - region.X), region.Width));
                region.Height = Math.Max(1, Math.Min(Math.Max(1, imageHeight - region.Y), region.Height));
                region.Translation = string.Empty;
                region.Reviewed = false;
                result.Add(region);
            }
            return result;
        }

        private static TextRegion MergePair(TextRegion local, TextRegion cloud)
        {
            var cloudTextWins = cloud.Confidence > local.Confidence + 0.12f ||
                                string.IsNullOrWhiteSpace(local.SourceText);
            var merged = Clone(cloudTextWins ? cloud : local);
            if (!cloudTextWins)
            {
                merged.TextColorArgb = cloud.TextColorArgb;
                merged.OutlineColorArgb = cloud.OutlineColorArgb;
                merged.HorizontalAlignment = cloud.HorizontalAlignment;
                merged.VerticalAlignment = cloud.VerticalAlignment;
                merged.VerticalText = cloud.VerticalText;
                if (cloud.FontSize >= 7f) merged.FontSize = cloud.FontSize;
            }

            merged.Confidence = Math.Max(local.Confidence, cloud.Confidence);
            merged.Translation = string.Empty;
            merged.Reviewed = false;
            return merged;
        }

        private static float MatchScore(TextRegion left, TextRegion right)
        {
            var intersection = Rectangle.Intersect(left.Bounds, right.Bounds);
            if (intersection.Width <= 0 || intersection.Height <= 0) return 0f;

            var intersectionArea = intersection.Width * (float)intersection.Height;
            var leftArea = Math.Max(1f, left.Width * (float)left.Height);
            var rightArea = Math.Max(1f, right.Width * (float)right.Height);
            var union = leftArea + rightArea - intersectionArea;
            var iou = intersectionArea / Math.Max(1f, union);
            var coverage = intersectionArea / Math.Min(leftArea, rightArea);
            var sameText = string.Equals(
                NormalizeText(left.SourceText),
                NormalizeText(right.SourceText),
                StringComparison.Ordinal);

            if (sameText && coverage >= 0.25f) return Math.Max(0.75f, coverage);
            return Math.Max(iou, coverage * 0.85f);
        }

        private static string NormalizeText(string value)
        {
            var builder = new StringBuilder();
            foreach (var character in (value ?? string.Empty).Normalize(NormalizationForm.FormKC))
            {
                if (char.IsLetterOrDigit(character)) builder.Append(character);
            }
            return builder.ToString();
        }

        private static TextRegion Clone(TextRegion source)
        {
            return new TextRegion
            {
                Id = source.Id,
                X = source.X,
                Y = source.Y,
                Width = source.Width,
                Height = source.Height,
                SourceText = source.SourceText,
                Translation = source.Translation,
                Confidence = source.Confidence,
                FontFamily = source.FontFamily,
                FontSize = source.FontSize,
                Bold = source.Bold,
                AutoFit = source.AutoFit,
                TextColorArgb = source.TextColorArgb,
                OutlineColorArgb = source.OutlineColorArgb,
                OutlineWidth = source.OutlineWidth,
                HorizontalAlignment = source.HorizontalAlignment,
                VerticalAlignment = source.VerticalAlignment,
                BackgroundMode = source.BackgroundMode,
                ClearPadding = source.ClearPadding,
                RepairMaskStrokes = CloneStrokes(source.RepairMaskStrokes),
                LetterSpacing = source.LetterSpacing,
                LineSpacing = source.LineSpacing,
                ShadowEnabled = source.ShadowEnabled,
                ShadowColorArgb = source.ShadowColorArgb,
                ShadowOffsetX = source.ShadowOffsetX,
                ShadowOffsetY = source.ShadowOffsetY,
                GlowWidth = source.GlowWidth,
                GlowColorArgb = source.GlowColorArgb,
                TextFillMode = source.TextFillMode,
                GradientEndColorArgb = source.GradientEndColorArgb,
                RotationDegrees = source.RotationDegrees,
                VerticalText = source.VerticalText,
                Reviewed = source.Reviewed
            };
        }

        private static List<RepairMaskStroke> CloneStrokes(IEnumerable<RepairMaskStroke> strokes)
        {
            return (strokes ?? Enumerable.Empty<RepairMaskStroke>())
                .Select(stroke => new RepairMaskStroke
                {
                    Eraser = stroke.Eraser,
                    Diameter = stroke.Diameter,
                    Points = (stroke.Points ?? new List<MaskPoint>())
                        .Select(point => new MaskPoint(point.X, point.Y))
                        .ToList()
                })
                .ToList();
        }
    }
}
