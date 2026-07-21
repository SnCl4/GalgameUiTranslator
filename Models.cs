using System;
using System.Collections.Generic;
using System.Drawing;

namespace GalgameUiTranslator
{
    public sealed class TranslationProject
    {
        public int Version { get; set; } = 3;
        public string SourceFolder { get; set; } = string.Empty;
        public string OutputFolder { get; set; } = string.Empty;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
        public List<string> ImportWarnings { get; set; } = new List<string>();
        public List<string> CustomFontFiles { get; set; } = new List<string>();
        public List<ImageDocument> Images { get; set; } = new List<ImageDocument>();
    }

    public sealed class ImageDocument
    {
        public string RelativePath { get; set; } = string.Empty;
        public int Width { get; set; }
        public int Height { get; set; }
        public string AtlasMetadataPath { get; set; } = string.Empty;
        public List<AtlasSprite> AtlasSprites { get; set; } = new List<AtlasSprite>();
        public List<TextRegion> Regions { get; set; } = new List<TextRegion>();

        public override string ToString()
        {
            return RelativePath + (Regions.Count > 0 ? $"  [{Regions.Count}]" : string.Empty);
        }
    }

    public sealed class AtlasSprite
    {
        public string Name { get; set; } = string.Empty;
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public bool Rotated { get; set; }

        public Rectangle Bounds => new Rectangle(X, Y, Width, Height);
    }

    public sealed class TextRegion
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; } = 160;
        public int Height { get; set; } = 48;
        public string SourceText { get; set; } = string.Empty;
        public string Translation { get; set; } = string.Empty;
        public float Confidence { get; set; } = 1.0f;
        public string FontFamily { get; set; } = "Microsoft YaHei";
        public float FontSize { get; set; } = 24f;
        public bool Bold { get; set; }
        public bool AutoFit { get; set; } = true;
        public int TextColorArgb { get; set; } = Color.White.ToArgb();
        public int OutlineColorArgb { get; set; } = Color.Black.ToArgb();
        public float OutlineWidth { get; set; } = 2f;
        public string HorizontalAlignment { get; set; } = "Center";
        public string VerticalAlignment { get; set; } = "Center";
        public string BackgroundMode { get; set; } = "Gradient";
        public int ClearPadding { get; set; } = 2;
        public List<RepairMaskStroke> RepairMaskStrokes { get; set; } = new List<RepairMaskStroke>();
        public float LetterSpacing { get; set; }
        public float LineSpacing { get; set; } = 1f;
        public bool ShadowEnabled { get; set; }
        public int ShadowColorArgb { get; set; } = Color.FromArgb(180, 0, 0, 0).ToArgb();
        public int ShadowOffsetX { get; set; } = 2;
        public int ShadowOffsetY { get; set; } = 2;
        public float GlowWidth { get; set; }
        public int GlowColorArgb { get; set; } = Color.White.ToArgb();
        public string TextFillMode { get; set; } = "Solid";
        public int GradientEndColorArgb { get; set; } = Color.FromArgb(255, 180, 220, 255).ToArgb();
        public float RotationDegrees { get; set; }
        public bool VerticalText { get; set; }
        public bool Reviewed { get; set; }

        public Rectangle Bounds => new Rectangle(X, Y, Width, Height);
    }

    public sealed class RepairMaskStroke
    {
        public bool Eraser { get; set; }
        public int Diameter { get; set; } = 18;
        public List<MaskPoint> Points { get; set; } = new List<MaskPoint>();
    }

    public sealed class MaskPoint
    {
        public MaskPoint()
        {
        }

        public MaskPoint(int x, int y)
        {
            X = x;
            Y = y;
        }

        public int X { get; set; }
        public int Y { get; set; }
    }

    public sealed class AppSettings
    {
        public string RecognitionMode { get; set; } = RecognitionModes.Local;
        public float LocalOcrMinimumConfidence { get; set; } = 0.35f;
        public string VisionApiBaseUrl { get; set; } = "https://api.openai.com/v1";
        public string VisionModel { get; set; } = "gpt-4.1-mini";
        public string TranslationApiBaseUrl { get; set; } = "https://api.deepseek.com";
        public string TranslationModel { get; set; } = "deepseek-v4-flash";
        public string DefaultFontFamily { get; set; } = "Microsoft YaHei";
        public string TranslationInstructions { get; set; } =
            "将图片中的日文游戏界面文字准确翻译成简体中文。保留专有名词、数字、符号和按钮语气；不要添加解释。";
    }

    public static class RecognitionModes
    {
        public const string Local = "Local";
        public const string LocalThenCloud = "LocalThenCloud";
        public const string Cloud = "Cloud";

        public static string Normalize(string value)
        {
            if (string.Equals(value, Cloud, StringComparison.OrdinalIgnoreCase)) return Cloud;
            if (string.Equals(value, LocalThenCloud, StringComparison.OrdinalIgnoreCase)) return LocalThenCloud;
            return Local;
        }

        public static bool UsesLocal(string value)
        {
            var mode = Normalize(value);
            return mode == Local || mode == LocalThenCloud;
        }

        public static bool UsesCloud(string value)
        {
            var mode = Normalize(value);
            return mode == Cloud || mode == LocalThenCloud;
        }
    }

    public sealed class TranslationResourceData
    {
        public int Version { get; set; } = 1;
        public List<TranslationMemoryEntry> Memory { get; set; } = new List<TranslationMemoryEntry>();
        public List<GlossaryEntry> Glossary { get; set; } = new List<GlossaryEntry>();
    }

    public sealed class TranslationMemoryEntry
    {
        public string Source { get; set; } = string.Empty;
        public string Translation { get; set; } = string.Empty;
        public int UseCount { get; set; }
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
    }

    public sealed class GlossaryEntry
    {
        public string Source { get; set; } = string.Empty;
        public string Translation { get; set; } = string.Empty;
        public string Note { get; set; } = string.Empty;
    }

    public sealed class TextStylePresetData
    {
        public int Version { get; set; } = 1;
        public List<TextStylePreset> Presets { get; set; } = new List<TextStylePreset>();
    }

    public sealed class TextStylePreset
    {
        public string Name { get; set; } = string.Empty;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
        public string FontFamily { get; set; } = "Microsoft YaHei";
        public float FontSize { get; set; } = 24f;
        public bool Bold { get; set; }
        public bool AutoFit { get; set; } = true;
        public int TextColorArgb { get; set; } = Color.White.ToArgb();
        public int OutlineColorArgb { get; set; } = Color.Black.ToArgb();
        public float OutlineWidth { get; set; } = 2f;
        public float LetterSpacing { get; set; }
        public float LineSpacing { get; set; } = 1f;
        public bool VerticalText { get; set; }
        public float RotationDegrees { get; set; }
        public bool ShadowEnabled { get; set; }
        public int ShadowColorArgb { get; set; } = Color.FromArgb(180, 0, 0, 0).ToArgb();
        public int ShadowOffsetX { get; set; } = 2;
        public int ShadowOffsetY { get; set; } = 2;
        public float GlowWidth { get; set; }
        public int GlowColorArgb { get; set; } = Color.White.ToArgb();
        public string TextFillMode { get; set; } = "Solid";
        public int GradientEndColorArgb { get; set; } = Color.FromArgb(255, 180, 220, 255).ToArgb();
        public string HorizontalAlignment { get; set; } = "Center";
        public string VerticalAlignment { get; set; } = "Center";

        public override string ToString() => Name;
    }

    public sealed class ApiAnalysisResult
    {
        public List<TextRegion> Regions { get; set; } = new List<TextRegion>();
        public string RawResponse { get; set; } = string.Empty;
        public string ProviderName { get; set; } = string.Empty;
        public bool UsedFallback { get; set; }
    }

    public sealed class AutosaveDocument
    {
        public int Version { get; set; } = 1;
        public DateTime SavedAt { get; set; } = DateTime.Now;
        public string OriginalProjectPath { get; set; } = string.Empty;
        public TranslationProject Project { get; set; } = new TranslationProject();
    }
}
