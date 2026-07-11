using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;

namespace GalgameUiTranslator
{
    public enum PreflightSeverity
    {
        Error,
        Warning,
        Info
    }

    public sealed class PreflightIssue
    {
        public PreflightSeverity Severity { get; set; }
        public string Code { get; set; } = string.Empty;
        public string ImagePath { get; set; } = string.Empty;
        public int? RegionIndex { get; set; }
        public string RegionId { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }

    public sealed class PreflightReport
    {
        public List<PreflightIssue> Issues { get; } = new List<PreflightIssue>();
        public int ErrorCount => Issues.Count(issue => issue.Severity == PreflightSeverity.Error);
        public int WarningCount => Issues.Count(issue => issue.Severity == PreflightSeverity.Warning);
        public int InfoCount => Issues.Count(issue => issue.Severity == PreflightSeverity.Info);
        public bool CanExport => ErrorCount == 0;
    }

    public static class PreflightService
    {
        public static PreflightReport Analyze(TranslationProject project)
        {
            var report = new PreflightReport();
            if (project == null)
            {
                Add(report, PreflightSeverity.Error, "PROJECT_MISSING", string.Empty, null, string.Empty,
                    "没有已打开的工程。");
                return report;
            }

            if (string.IsNullOrWhiteSpace(project.SourceFolder) || !Directory.Exists(project.SourceFolder))
            {
                Add(report, PreflightSeverity.Error, "SOURCE_FOLDER_MISSING", string.Empty, null, string.Empty,
                    "源图片目录不存在。");
                return report;
            }

            if (project.Images.Count == 0)
            {
                Add(report, PreflightSeverity.Error, "NO_IMAGES", string.Empty, null, string.Empty,
                    "工程中没有图片。");
                return report;
            }

            foreach (var fontPath in project.CustomFontFiles.Where(path => !string.IsNullOrWhiteSpace(path) && !File.Exists(path)))
            {
                Add(report, PreflightSeverity.Warning, "CUSTOM_FONT_FILE_MISSING", string.Empty, null, string.Empty,
                    "外部字体文件已丢失：" + fontPath);
            }

            foreach (var metadataPath in project.Images
                         .Select(image => image.AtlasMetadataPath)
                         .Where(path => !string.IsNullOrWhiteSpace(path))
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var fullPath = ProjectService.GetSafeOutputPath(project.SourceFolder, metadataPath);
                    if (!File.Exists(fullPath))
                        Add(report, PreflightSeverity.Error, "ATLAS_METADATA_MISSING", metadataPath, null, string.Empty,
                            "图集元数据文件不存在，无法随导出结果复制。 ");
                }
                catch (Exception exception)
                {
                    Add(report, PreflightSeverity.Error, "ATLAS_METADATA_PATH_INVALID", metadataPath, null, string.Empty,
                        "图集元数据路径无效：" + exception.Message);
                }
            }

            var duplicateIds = project.Images
                .SelectMany(image => image.Regions)
                .Where(region => !string.IsNullOrWhiteSpace(region.Id))
                .GroupBy(region => region.Id, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var image in project.Images)
            {
                var sourcePath = ProjectService.GetSourcePath(project, image);
                if (!File.Exists(sourcePath))
                {
                    Add(report, PreflightSeverity.Error, "SOURCE_IMAGE_MISSING", image.RelativePath, null, string.Empty,
                        "源图片文件不存在。");
                    continue;
                }

                try
                {
                    var source = ImageProcessor.ReadMetadata(sourcePath);
                    if (source.Width != image.Width || source.Height != image.Height)
                    {
                        Add(report, PreflightSeverity.Error, "SOURCE_DIMENSIONS_CHANGED", image.RelativePath, null, string.Empty,
                            $"源图片尺寸已从工程记录的 {image.Width}×{image.Height} 变为 {source.Width}×{source.Height}。");
                    }
                }
                catch (Exception exception)
                {
                    Add(report, PreflightSeverity.Error, "SOURCE_IMAGE_INVALID", image.RelativePath, null, string.Empty,
                        "无法读取源图片：" + exception.Message);
                    continue;
                }

                foreach (var sprite in image.AtlasSprites ?? Enumerable.Empty<AtlasSprite>())
                {
                    if (sprite.Width <= 0 || sprite.Height <= 0 || sprite.X < 0 || sprite.Y < 0 ||
                        sprite.X + sprite.Width > image.Width || sprite.Y + sprite.Height > image.Height)
                    {
                        Add(report, PreflightSeverity.Error, "ATLAS_SPRITE_OUT_OF_BOUNDS", image.RelativePath, null, string.Empty,
                            $"图集精灵“{sprite.Name}”超出图片边界。 ");
                    }
                }

                for (var index = 0; index < image.Regions.Count; index++)
                {
                    AnalyzeRegion(report, image, image.Regions[index], index + 1, duplicateIds);
                }
            }

            return report;
        }

        public static string ValidateExportedFile(string sourcePath, string outputPath, ImageDocument document)
        {
            if (!File.Exists(outputPath)) return "导出文件没有生成。";
            try
            {
                var source = ImageProcessor.ReadMetadata(sourcePath);
                var output = ImageProcessor.ReadMetadata(outputPath);
                if (output.Width != document.Width || output.Height != document.Height)
                {
                    return $"导出尺寸异常：期望 {document.Width}×{document.Height}，实际 {output.Width}×{output.Height}。";
                }

                if (source.HasAlpha && !output.HasAlpha)
                {
                    return "源图片包含 Alpha 通道，但导出图片不再包含 Alpha 通道。";
                }

                if (Path.GetExtension(sourcePath).Equals(".dds", StringComparison.OrdinalIgnoreCase) &&
                    (!string.Equals(source.FormatName, output.FormatName, StringComparison.OrdinalIgnoreCase) ||
                     source.MipMapCount != output.MipMapCount))
                {
                    return $"DDS 格式参数发生变化：源文件 {source.FormatName}/{source.MipMapCount} 级 mipmap，" +
                           $"导出文件 {output.FormatName}/{output.MipMapCount} 级。";
                }

                return string.Empty;
            }
            catch (Exception exception)
            {
                return "无法验证导出文件：" + exception.Message;
            }
        }

        private static void AnalyzeRegion(
            PreflightReport report,
            ImageDocument image,
            TextRegion region,
            int regionIndex,
            HashSet<string> duplicateIds)
        {
            if (string.IsNullOrWhiteSpace(region.Id))
            {
                Add(report, PreflightSeverity.Error, "REGION_ID_MISSING", image.RelativePath, regionIndex, string.Empty,
                    "文字区域缺少稳定 ID。");
            }
            else if (duplicateIds.Contains(region.Id))
            {
                Add(report, PreflightSeverity.Error, "REGION_ID_DUPLICATE", image.RelativePath, regionIndex, region.Id,
                    "文字区域 ID 与其他区域重复。");
            }

            if (region.Width <= 0 || region.Height <= 0 || region.X < 0 || region.Y < 0 ||
                region.X + region.Width > image.Width || region.Y + region.Height > image.Height)
            {
                Add(report, PreflightSeverity.Error, "REGION_OUT_OF_BOUNDS", image.RelativePath, regionIndex, region.Id,
                    $"文字区域 ({region.X},{region.Y},{region.Width},{region.Height}) 超出 {image.Width}×{image.Height} 画布。");
            }

            if (string.IsNullOrWhiteSpace(region.SourceText))
            {
                Add(report, PreflightSeverity.Warning, "SOURCE_TEXT_EMPTY", image.RelativePath, regionIndex, region.Id,
                    "日文原文为空，无法核对翻译来源。");
            }

            if (string.IsNullOrWhiteSpace(region.Translation))
            {
                Add(report, PreflightSeverity.Error, "TRANSLATION_EMPTY", image.RelativePath, regionIndex, region.Id,
                    "中文译文为空；如不需要处理，请删除此区域。");
            }
            else
            {
                if (!FontManager.HasFontFamily(region.FontFamily))
                {
                    Add(report, PreflightSeverity.Error, "FONT_MISSING", image.RelativePath, regionIndex, region.Id,
                        "找不到字体：" + region.FontFamily);
                }
                else
                {
                    try
                    {
                        if (!ImageProcessor.CheckTextFits(region, out var fittedSize))
                        {
                            Add(report, PreflightSeverity.Error, "TEXT_OVERFLOW", image.RelativePath, regionIndex, region.Id,
                                $"文字在最小字号 {fittedSize:0.#}px 下仍然超出区域。");
                        }
                        else if (region.AutoFit && fittedSize < 9f)
                        {
                            Add(report, PreflightSeverity.Warning, "TEXT_TOO_SMALL", image.RelativePath, regionIndex, region.Id,
                                $"自动适配后字号仅为 {fittedSize:0.#}px，游戏内可能难以阅读。");
                        }
                    }
                    catch (Exception exception)
                    {
                        Add(report, PreflightSeverity.Error, "TEXT_LAYOUT_FAILED", image.RelativePath, regionIndex, region.Id,
                            "无法验证文字排版：" + exception.Message);
                    }
                }
            }

            if (!region.Reviewed)
            {
                var message = region.Confidence < 0.75f
                    ? $"识别置信度仅 {region.Confidence:P0}，且尚未人工校对。"
                    : "该区域尚未标记为人工校对。";
                Add(report, PreflightSeverity.Warning, "NOT_REVIEWED", image.RelativePath, regionIndex, region.Id, message);
            }
        }

        private static void Add(
            PreflightReport report,
            PreflightSeverity severity,
            string code,
            string imagePath,
            int? regionIndex,
            string regionId,
            string message)
        {
            report.Issues.Add(new PreflightIssue
            {
                Severity = severity,
                Code = code,
                ImagePath = imagePath,
                RegionIndex = regionIndex,
                RegionId = regionId,
                Message = message
            });
        }
    }
}
