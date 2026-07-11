using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace GalgameUiTranslator
{
    public sealed class TextStylePresetService
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        private readonly string _storagePath;

        public TextStylePresetService(TextStylePresetData data = null, string storagePath = null)
        {
            _storagePath = storagePath;
            Data = Clean(data ?? new TextStylePresetData());
        }

        public TextStylePresetData Data { get; private set; }

        public IReadOnlyList<TextStylePreset> Presets => Data.Presets;

        public bool IsDirty { get; private set; }

        public static TextStylePresetService LoadDefault()
        {
            return Load(GetDefaultPath(), true);
        }

        public static TextStylePresetService Load(string path, bool createDefaultsWhenMissing = false)
        {
            try
            {
                if (!File.Exists(path))
                {
                    var initial = createDefaultsWhenMissing ? CreateDefaultData() : new TextStylePresetData();
                    return new TextStylePresetService(initial, path);
                }

                var data = JsonSerializer.Deserialize<TextStylePresetData>(
                    File.ReadAllText(path, Encoding.UTF8), JsonOptions);
                return new TextStylePresetService(data, path);
            }
            catch
            {
                var fallback = createDefaultsWhenMissing ? CreateDefaultData() : new TextStylePresetData();
                return new TextStylePresetService(fallback, path);
            }
        }

        public static string GetDefaultPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GalgameUiTranslator",
                "text-style-presets.json");
        }

        public TextStylePreset Upsert(string name, TextRegion source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var cleanName = (name ?? string.Empty).Trim();
            if (cleanName.Length == 0) throw new ArgumentException("预设名称不能为空。", nameof(name));
            if (cleanName.Length > 60) cleanName = cleanName.Substring(0, 60);

            var preset = Capture(cleanName, source);
            var index = Data.Presets.FindIndex(item =>
                string.Equals(item.Name, cleanName, StringComparison.CurrentCultureIgnoreCase));
            if (index >= 0) Data.Presets[index] = preset;
            else Data.Presets.Add(preset);
            SortPresets();
            IsDirty = true;
            return preset;
        }

        public bool Delete(string name)
        {
            var index = Data.Presets.FindIndex(item =>
                string.Equals(item.Name, name, StringComparison.CurrentCultureIgnoreCase));
            if (index < 0) return false;
            Data.Presets.RemoveAt(index);
            IsDirty = true;
            return true;
        }

        public void Save()
        {
            if (string.IsNullOrWhiteSpace(_storagePath))
            {
                IsDirty = false;
                return;
            }

            var directory = Path.GetDirectoryName(_storagePath);
            if (string.IsNullOrWhiteSpace(directory))
                throw new InvalidOperationException("文字样式预设保存路径缺少有效目录。");
            Directory.CreateDirectory(directory);
            var tempPath = _storagePath + ".tmp-" + Guid.NewGuid().ToString("N");
            File.WriteAllText(tempPath, JsonSerializer.Serialize(Data, JsonOptions), new UTF8Encoding(false));
            try
            {
                if (File.Exists(_storagePath)) File.Replace(tempPath, _storagePath, _storagePath + ".bak", true);
                else File.Move(tempPath, _storagePath);
                IsDirty = false;
            }
            catch (PlatformNotSupportedException)
            {
                File.Copy(tempPath, _storagePath, true);
                File.Delete(tempPath);
                IsDirty = false;
            }
            catch (IOException)
            {
                File.Copy(tempPath, _storagePath, true);
                File.Delete(tempPath);
                IsDirty = false;
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }

        public static TextStylePreset Capture(string name, TextRegion source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            return Normalize(new TextStylePreset
            {
                Name = (name ?? string.Empty).Trim(),
                UpdatedAt = DateTime.Now,
                FontFamily = source.FontFamily,
                FontSize = source.FontSize,
                Bold = source.Bold,
                AutoFit = source.AutoFit,
                TextColorArgb = source.TextColorArgb,
                OutlineColorArgb = source.OutlineColorArgb,
                OutlineWidth = source.OutlineWidth,
                LetterSpacing = source.LetterSpacing,
                LineSpacing = source.LineSpacing,
                VerticalText = source.VerticalText,
                RotationDegrees = source.RotationDegrees,
                ShadowEnabled = source.ShadowEnabled,
                ShadowColorArgb = source.ShadowColorArgb,
                ShadowOffsetX = source.ShadowOffsetX,
                ShadowOffsetY = source.ShadowOffsetY,
                GlowWidth = source.GlowWidth,
                GlowColorArgb = source.GlowColorArgb,
                TextFillMode = source.TextFillMode,
                GradientEndColorArgb = source.GradientEndColorArgb,
                HorizontalAlignment = source.HorizontalAlignment,
                VerticalAlignment = source.VerticalAlignment
            });
        }

        public static void Apply(TextStylePreset preset, TextRegion target)
        {
            if (preset == null) throw new ArgumentNullException(nameof(preset));
            if (target == null) throw new ArgumentNullException(nameof(target));
            preset = Normalize(preset);
            target.FontFamily = preset.FontFamily;
            target.FontSize = preset.FontSize;
            target.Bold = preset.Bold;
            target.AutoFit = preset.AutoFit;
            target.TextColorArgb = preset.TextColorArgb;
            target.OutlineColorArgb = preset.OutlineColorArgb;
            target.OutlineWidth = preset.OutlineWidth;
            target.LetterSpacing = preset.LetterSpacing;
            target.LineSpacing = preset.LineSpacing;
            target.VerticalText = preset.VerticalText;
            target.RotationDegrees = preset.RotationDegrees;
            target.ShadowEnabled = preset.ShadowEnabled;
            target.ShadowColorArgb = preset.ShadowColorArgb;
            target.ShadowOffsetX = preset.ShadowOffsetX;
            target.ShadowOffsetY = preset.ShadowOffsetY;
            target.GlowWidth = preset.GlowWidth;
            target.GlowColorArgb = preset.GlowColorArgb;
            target.TextFillMode = preset.TextFillMode;
            target.GradientEndColorArgb = preset.GradientEndColorArgb;
            target.HorizontalAlignment = preset.HorizontalAlignment;
            target.VerticalAlignment = preset.VerticalAlignment;
        }

        public static TextStylePresetData CreateDefaultData()
        {
            return new TextStylePresetData
            {
                Presets = new List<TextStylePreset>
                {
                    new TextStylePreset
                    {
                        Name = "白字黑描边",
                        FontSize = 24f,
                        AutoFit = true,
                        TextColorArgb = Color.White.ToArgb(),
                        OutlineColorArgb = Color.Black.ToArgb(),
                        OutlineWidth = 2f
                    },
                    new TextStylePreset
                    {
                        Name = "标题金色渐变",
                        FontSize = 34f,
                        Bold = true,
                        AutoFit = true,
                        TextColorArgb = Color.FromArgb(255, 255, 240, 160).ToArgb(),
                        OutlineColorArgb = Color.FromArgb(255, 70, 40, 15).ToArgb(),
                        OutlineWidth = 2.5f,
                        ShadowEnabled = true,
                        ShadowColorArgb = Color.FromArgb(180, 0, 0, 0).ToArgb(),
                        ShadowOffsetX = 2,
                        ShadowOffsetY = 3,
                        TextFillMode = "VerticalGradient",
                        GradientEndColorArgb = Color.FromArgb(255, 225, 145, 45).ToArgb()
                    },
                    new TextStylePreset
                    {
                        Name = "黑字浅色面板",
                        FontSize = 24f,
                        Bold = true,
                        AutoFit = true,
                        TextColorArgb = Color.FromArgb(255, 28, 35, 48).ToArgb(),
                        OutlineColorArgb = Color.White.ToArgb(),
                        OutlineWidth = 0f
                    },
                    new TextStylePreset
                    {
                        Name = "霓虹蓝发光",
                        FontSize = 28f,
                        Bold = true,
                        AutoFit = true,
                        TextColorArgb = Color.White.ToArgb(),
                        OutlineColorArgb = Color.FromArgb(255, 15, 45, 90).ToArgb(),
                        OutlineWidth = 1.5f,
                        GlowWidth = 3f,
                        GlowColorArgb = Color.FromArgb(255, 70, 205, 255).ToArgb(),
                        TextFillMode = "VerticalGradient",
                        GradientEndColorArgb = Color.FromArgb(255, 80, 210, 255).ToArgb()
                    }
                }
            };
        }

        private static TextStylePresetData Clean(TextStylePresetData data)
        {
            var presets = (data.Presets ?? new List<TextStylePreset>())
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Name))
                .Select(Normalize)
                .GroupBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(group => group.Last())
                .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            return new TextStylePresetData
            {
                Version = Math.Max(1, data.Version),
                Presets = presets
            };
        }

        private static TextStylePreset Normalize(TextStylePreset source)
        {
            return new TextStylePreset
            {
                Name = (source.Name ?? string.Empty).Trim(),
                UpdatedAt = source.UpdatedAt == default(DateTime) ? DateTime.Now : source.UpdatedAt,
                FontFamily = string.IsNullOrWhiteSpace(source.FontFamily) ? "Microsoft YaHei" : source.FontFamily.Trim(),
                FontSize = Clamp(source.FontSize, 6f, 300f),
                Bold = source.Bold,
                AutoFit = source.AutoFit,
                TextColorArgb = source.TextColorArgb,
                OutlineColorArgb = source.OutlineColorArgb,
                OutlineWidth = Clamp(source.OutlineWidth, 0f, 20f),
                LetterSpacing = Clamp(source.LetterSpacing, -10f, 50f),
                LineSpacing = Clamp(source.LineSpacing <= 0f ? 1f : source.LineSpacing, 0.5f, 3f),
                VerticalText = source.VerticalText,
                RotationDegrees = Clamp(source.RotationDegrees, -180f, 180f),
                ShadowEnabled = source.ShadowEnabled,
                ShadowColorArgb = source.ShadowColorArgb,
                ShadowOffsetX = Clamp(source.ShadowOffsetX, -50, 50),
                ShadowOffsetY = Clamp(source.ShadowOffsetY, -50, 50),
                GlowWidth = Clamp(source.GlowWidth, 0f, 30f),
                GlowColorArgb = source.GlowColorArgb,
                TextFillMode = NormalizeChoice(source.TextFillMode, "Solid", "VerticalGradient"),
                GradientEndColorArgb = source.GradientEndColorArgb,
                HorizontalAlignment = NormalizeChoice(source.HorizontalAlignment, "Center", "Left", "Right"),
                VerticalAlignment = NormalizeChoice(source.VerticalAlignment, "Center", "Top", "Bottom")
            };
        }

        private void SortPresets()
        {
            Data.Presets = Data.Presets
                .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        private static string NormalizeChoice(string value, string fallback, params string[] alternatives)
        {
            if (string.Equals(value, fallback, StringComparison.OrdinalIgnoreCase)) return fallback;
            foreach (var alternative in alternatives)
                if (string.Equals(value, alternative, StringComparison.OrdinalIgnoreCase)) return alternative;
            return fallback;
        }

        private static float Clamp(float value, float minimum, float maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }

        private static int Clamp(int value, int minimum, int maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }
    }
}
