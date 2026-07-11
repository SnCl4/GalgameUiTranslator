using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GalgameUiTranslator
{
    public static class ProjectService
    {
        private static readonly HashSet<string> SupportedExtensions = new HashSet<string>(
            new[] { ".png", ".jpg", ".jpeg", ".bmp", ".dds" },
            StringComparer.OrdinalIgnoreCase);

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        public static TranslationProject CreateFromFolder(string folder)
        {
            var root = Path.GetFullPath(folder);
            var project = new TranslationProject { SourceFolder = root };

            foreach (var path in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
                         .Where(path => SupportedExtensions.Contains(Path.GetExtension(path)))
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var metadata = ImageProcessor.ReadMetadata(path);
                    project.Images.Add(new ImageDocument
                    {
                        RelativePath = GetRelativePath(root, path),
                        Width = metadata.Width,
                        Height = metadata.Height
                    });
                }
                catch (Exception exception)
                {
                    project.ImportWarnings.Add(GetRelativePath(root, path) + "：" + exception.Message);
                }
            }

            AtlasService.RefreshProject(project);

            return project;
        }

        public static void SaveProject(string path, TranslationProject project)
        {
            SaveProject(path, project, true, true);
        }

        public static void SaveProject(
            string path,
            TranslationProject project,
            bool createBackup,
            bool updateTimestamp)
        {
            if (updateTimestamp) project.UpdatedAt = DateTime.Now;
            WriteAtomic(path, SerializeProject(project), createBackup);
        }

        public static TranslationProject LoadProject(string path)
        {
            return DeserializeProject(File.ReadAllText(path));
        }

        public static string SerializeProject(TranslationProject project)
        {
            return JsonSerializer.Serialize(project, JsonOptions);
        }

        public static TranslationProject DeserializeProject(string json)
        {
            var project = JsonSerializer.Deserialize<TranslationProject>(json, JsonOptions);
            if (project == null)
            {
                throw new InvalidDataException("工程文件内容为空或格式不正确。");
            }

            project.Images = project.Images ?? new List<ImageDocument>();
            project.ImportWarnings = project.ImportWarnings ?? new List<string>();
            project.CustomFontFiles = project.CustomFontFiles ?? new List<string>();
            foreach (var image in project.Images)
            {
                image.Regions = image.Regions ?? new List<TextRegion>();
                image.AtlasSprites = image.AtlasSprites ?? new List<AtlasSprite>();
                foreach (var region in image.Regions)
                {
                    region.RepairMaskStrokes = region.RepairMaskStrokes ?? new List<RepairMaskStroke>();
                    foreach (var stroke in region.RepairMaskStrokes)
                        stroke.Points = stroke.Points ?? new List<MaskPoint>();
                    if (region.LineSpacing <= 0f) region.LineSpacing = 1f;
                }
            }

            project.Version = Math.Max(project.Version, 3);

            return project;
        }

        public static string GetAutosaveDirectory()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GalgameUiTranslator",
                "Autosave");
        }

        public static string GetAutosavePath(TranslationProject project, string originalProjectPath)
        {
            var identity = !string.IsNullOrWhiteSpace(originalProjectPath)
                ? Path.GetFullPath(originalProjectPath)
                : Path.GetFullPath(project.SourceFolder ?? string.Empty);
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(identity.ToLowerInvariant()));
                var token = BitConverter.ToString(hash, 0, 8).Replace("-", string.Empty);
                return Path.Combine(GetAutosaveDirectory(), token + ".guih.autosave.json");
            }
        }

        public static void SaveAutosave(string path, TranslationProject project, string originalProjectPath)
        {
            var document = new AutosaveDocument
            {
                SavedAt = DateTime.Now,
                OriginalProjectPath = originalProjectPath ?? string.Empty,
                Project = project
            };
            WriteAtomic(path, JsonSerializer.Serialize(document, JsonOptions), false);
        }

        public static AutosaveDocument LoadAutosave(string path)
        {
            var document = JsonSerializer.Deserialize<AutosaveDocument>(File.ReadAllText(path), JsonOptions);
            if (document == null || document.Project == null)
                throw new InvalidDataException("自动恢复文件内容为空或格式不正确。");
            document.Project = DeserializeProject(SerializeProject(document.Project));
            return document;
        }

        public static IReadOnlyList<string> FindAutosaves()
        {
            var folder = GetAutosaveDirectory();
            if (!Directory.Exists(folder)) return Array.Empty<string>();
            return Directory.EnumerateFiles(folder, "*.guih.autosave.json", SearchOption.TopDirectoryOnly)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ToArray();
        }

        public static void DeleteAutosave(string path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) File.Delete(path);
            }
            catch
            {
                // A stale recovery file is preferable to failing a normal save or close.
            }
        }

        public static void SaveSettings(AppSettings settings)
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GalgameUiTranslator");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "settings.json"),
                JsonSerializer.Serialize(settings, JsonOptions));
        }

        public static AppSettings LoadSettings()
        {
            try
            {
                var path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "GalgameUiTranslator", "settings.json");
                return File.Exists(path)
                    ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions) ?? new AppSettings()
                    : new AppSettings();
            }
            catch
            {
                return new AppSettings();
            }
        }

        public static string GetSourcePath(TranslationProject project, ImageDocument document)
        {
            return GetSafeOutputPath(project.SourceFolder, document.RelativePath);
        }

        public static string GetSafeOutputPath(string outputRoot, string relativePath)
        {
            var root = Path.GetFullPath(outputRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (root.Length == 2 && root[1] == ':') root += Path.DirectorySeparatorChar;
            var fullPath = Path.GetFullPath(Path.Combine(root, relativePath ?? string.Empty));
            var prefix = root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? root
                : root + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("输出相对路径超出了目标目录：" + relativePath);
            return fullPath;
        }

        private static void WriteAtomic(string path, string content, bool createBackup)
        {
            var fullPath = Path.GetFullPath(path);
            var directory = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrWhiteSpace(directory))
                throw new InvalidOperationException("保存路径缺少有效目录。");
            Directory.CreateDirectory(directory);
            var tempPath = fullPath + ".tmp-" + Guid.NewGuid().ToString("N");
            File.WriteAllText(tempPath, content, new UTF8Encoding(false));
            try
            {
                if (File.Exists(fullPath))
                {
                    var backupPath = createBackup ? fullPath + ".bak" : null;
                    try
                    {
                        File.Replace(tempPath, fullPath, backupPath, true);
                    }
                    catch (PlatformNotSupportedException)
                    {
                        if (createBackup) File.Copy(fullPath, fullPath + ".bak", true);
                        File.Copy(tempPath, fullPath, true);
                    }
                    catch (IOException)
                    {
                        if (createBackup) File.Copy(fullPath, fullPath + ".bak", true);
                        File.Copy(tempPath, fullPath, true);
                    }
                }
                else
                {
                    File.Move(tempPath, fullPath);
                }
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }

        internal static string GetRelativePath(string root, string path)
        {
            var rootUri = new Uri(AppendDirectorySeparator(root));
            var pathUri = new Uri(path);
            return Uri.UnescapeDataString(rootUri.MakeRelativeUri(pathUri).ToString())
                .Replace('/', Path.DirectorySeparatorChar);
        }

        private static string AppendDirectorySeparator(string path)
        {
            return path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? path
                : path + Path.DirectorySeparatorChar;
        }
    }
}
