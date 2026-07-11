using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GalgameUiTranslator
{
    public sealed class BatchQueueDocument
    {
        public int Version { get; set; } = 1;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
        public string SourceFolder { get; set; } = string.Empty;
        public string ProjectPath { get; set; } = string.Empty;
        public List<BatchTaskRecord> Items { get; set; } = new List<BatchTaskRecord>();
    }

    public sealed class BatchTaskRecord
    {
        public string Id { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public BatchTaskKind Kind { get; set; }
        public string Target { get; set; } = string.Empty;
        public BatchTaskStatus Status { get; set; }
        public int Attempts { get; set; }
        public string Message { get; set; } = string.Empty;
        public string ImageRelativePath { get; set; } = string.Empty;
        public List<string> RegionIds { get; set; } = new List<string>();
        public string OutputRoot { get; set; } = string.Empty;
        public string MetadataRelativePath { get; set; } = string.Empty;
        public int ResultCount { get; set; }
        public int MemoryMatchCount { get; set; }
    }

    public static class BatchTaskPersistenceService
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        public static string GetQueuePath(string sourceFolder)
        {
            var identity = NormalizeFolder(sourceFolder).ToLowerInvariant();
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(identity));
                var token = BitConverter.ToString(hash, 0, 8).Replace("-", string.Empty);
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "GalgameUiTranslator",
                    "BatchQueue",
                    token + ".batch.json");
            }
        }

        public static void Save(
            string path,
            string sourceFolder,
            string projectPath,
            IEnumerable<BatchTaskItem> items)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("队列路径不能为空。", nameof(path));
            if (items == null) throw new ArgumentNullException(nameof(items));
            var records = items.Select(ToRecord).ToList();
            if (records.Count == 0)
            {
                Delete(path);
                return;
            }

            var document = new BatchQueueDocument
            {
                UpdatedAt = DateTime.Now,
                SourceFolder = NormalizeFolder(sourceFolder),
                ProjectPath = projectPath ?? string.Empty,
                Items = records
            };
            WriteAtomic(path, JsonSerializer.Serialize(document, JsonOptions));
        }

        public static IReadOnlyList<BatchTaskItem> Load(string path, string expectedSourceFolder)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return Array.Empty<BatchTaskItem>();
            var document = JsonSerializer.Deserialize<BatchQueueDocument>(File.ReadAllText(path), JsonOptions);
            if (document == null || document.Items == null)
                throw new InvalidDataException("批量任务恢复文件为空或格式不正确。");
            if (!string.Equals(
                    NormalizeFolder(document.SourceFolder),
                    NormalizeFolder(expectedSourceFolder),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("批量任务恢复文件与当前图片目录不匹配。");
            }
            return document.Items.Select(ToItem).ToArray();
        }

        public static void Delete(string path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) File.Delete(path);
            }
            catch
            {
                // A stale queue file is safer than interrupting normal editing.
            }
        }

        private static BatchTaskRecord ToRecord(BatchTaskItem item)
        {
            return new BatchTaskRecord
            {
                Id = item.Id,
                CreatedAt = item.CreatedAt,
                Kind = item.Kind,
                Target = item.Target,
                Status = item.Status,
                Attempts = item.Attempts,
                Message = item.Message,
                ImageRelativePath = item.ImageRelativePath,
                RegionIds = (item.RegionIds ?? new List<string>()).ToList(),
                OutputRoot = item.OutputRoot,
                MetadataRelativePath = item.MetadataRelativePath,
                ResultCount = item.ResultCount,
                MemoryMatchCount = item.MemoryMatchCount
            };
        }

        private static BatchTaskItem ToItem(BatchTaskRecord record)
        {
            return new BatchTaskItem
            {
                Id = string.IsNullOrWhiteSpace(record.Id) ? Guid.NewGuid().ToString("N") : record.Id,
                CreatedAt = record.CreatedAt == default(DateTime) ? DateTime.Now : record.CreatedAt,
                Kind = record.Kind,
                Target = record.Target ?? string.Empty,
                Status = record.Status,
                Attempts = record.Attempts,
                Message = record.Message ?? string.Empty,
                ImageRelativePath = record.ImageRelativePath ?? string.Empty,
                RegionIds = record.RegionIds ?? new List<string>(),
                OutputRoot = record.OutputRoot ?? string.Empty,
                MetadataRelativePath = record.MetadataRelativePath ?? string.Empty,
                ResultCount = record.ResultCount,
                MemoryMatchCount = record.MemoryMatchCount
            };
        }

        private static string NormalizeFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new InvalidDataException("批量任务缺少源图片目录。");
            var fullPath = Path.GetFullPath(path);
            var root = Path.GetPathRoot(fullPath);
            return string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase)
                ? fullPath
                : fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static void WriteAtomic(string path, string content)
        {
            var fullPath = Path.GetFullPath(path);
            var directory = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrWhiteSpace(directory))
                throw new InvalidOperationException("批量任务恢复路径缺少有效目录。");
            Directory.CreateDirectory(directory);
            var tempPath = fullPath + ".tmp-" + Guid.NewGuid().ToString("N");
            File.WriteAllText(tempPath, content, new UTF8Encoding(false));
            try
            {
                if (File.Exists(fullPath))
                {
                    try { File.Replace(tempPath, fullPath, null, true); }
                    catch (PlatformNotSupportedException) { File.Copy(tempPath, fullPath, true); }
                    catch (IOException) { File.Copy(tempPath, fullPath, true); }
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
    }
}
