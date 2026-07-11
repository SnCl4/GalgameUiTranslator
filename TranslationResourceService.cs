using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace GalgameUiTranslator
{
    public sealed class TranslationResourceService
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        private readonly string _storagePath;

        public TranslationResourceService(TranslationResourceData data = null, string storagePath = null)
        {
            _storagePath = storagePath;
            Data = Clean(data ?? new TranslationResourceData());
        }

        public TranslationResourceData Data { get; private set; }

        public bool IsDirty { get; private set; }

        public IReadOnlyList<GlossaryEntry> Glossary => Data.Glossary;

        public static TranslationResourceService LoadDefault()
        {
            return Load(GetDefaultPath());
        }

        public static TranslationResourceService Load(string path)
        {
            try
            {
                if (!File.Exists(path)) return new TranslationResourceService(null, path);
                var data = JsonSerializer.Deserialize<TranslationResourceData>(
                    File.ReadAllText(path, Encoding.UTF8), JsonOptions);
                return new TranslationResourceService(data, path);
            }
            catch
            {
                return new TranslationResourceService(null, path);
            }
        }

        public static string GetDefaultPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GalgameUiTranslator",
                "translation-resources.json");
        }

        public int ApplyExactMatches(IEnumerable<TextRegion> regions, ISet<string> matchedRegionIds = null)
        {
            var lookup = Data.Memory
                .Where(IsValidMemory)
                .GroupBy(entry => Normalize(entry.Source), StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
            var count = 0;
            foreach (var region in regions ?? Enumerable.Empty<TextRegion>())
            {
                if (region == null) continue;
                var key = Normalize(region.SourceText);
                if (key.Length == 0 || !lookup.TryGetValue(key, out var entry)) continue;
                region.Translation = entry.Translation.Trim();
                entry.UseCount++;
                entry.UpdatedAt = DateTime.Now;
                matchedRegionIds?.Add(region.Id);
                count++;
                IsDirty = true;
            }
            return count;
        }

        public int CollectReviewed(TranslationProject project)
        {
            if (project == null) return 0;
            var changed = 0;
            foreach (var region in project.Images
                         .Where(image => image != null)
                         .SelectMany(image => image.Regions ?? new List<TextRegion>())
                         .Where(region => region != null && region.Reviewed))
            {
                if (Remember(region.SourceText, region.Translation)) changed++;
            }
            return changed;
        }

        public bool Remember(string source, string translation)
        {
            var key = Normalize(source);
            var value = (translation ?? string.Empty).Trim();
            if (key.Length == 0 || value.Length == 0) return false;

            var existing = Data.Memory.LastOrDefault(entry =>
                string.Equals(Normalize(entry.Source), key, StringComparison.Ordinal));
            if (existing != null)
            {
                if (string.Equals(existing.Translation?.Trim(), value, StringComparison.Ordinal)) return false;
                existing.Source = (source ?? string.Empty).Trim();
                existing.Translation = value;
                existing.UpdatedAt = DateTime.Now;
            }
            else
            {
                Data.Memory.Add(new TranslationMemoryEntry
                {
                    Source = (source ?? string.Empty).Trim(),
                    Translation = value,
                    UpdatedAt = DateTime.Now
                });
            }

            IsDirty = true;
            return true;
        }

        public IReadOnlyList<GlossaryEntry> GetRelevantGlossary(IEnumerable<TextRegion> regions, int maximum = 200)
        {
            var sources = (regions ?? Enumerable.Empty<TextRegion>())
                .Where(region => region != null && !string.IsNullOrWhiteSpace(region.SourceText))
                .Select(region => Normalize(region.SourceText))
                .ToArray();
            if (sources.Length == 0) return Array.Empty<GlossaryEntry>();

            return Data.Glossary
                .Where(IsValidGlossary)
                .Where(entry =>
                {
                    var term = Normalize(entry.Source);
                    return sources.Any(source => source.IndexOf(term, StringComparison.Ordinal) >= 0);
                })
                .Take(Math.Max(0, maximum))
                .ToArray();
        }

        public void ReplaceData(TranslationResourceData data)
        {
            Data = Clean(data ?? new TranslationResourceData());
            IsDirty = true;
        }

        public TranslationResourceData CloneData()
        {
            return JsonSerializer.Deserialize<TranslationResourceData>(
                       JsonSerializer.Serialize(Data, JsonOptions), JsonOptions)
                   ?? new TranslationResourceData();
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
                throw new InvalidOperationException("翻译资源保存路径缺少有效目录。");
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

        public static string Normalize(string value)
        {
            return (value ?? string.Empty)
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Trim()
                .Normalize(NormalizationForm.FormKC);
        }

        private static TranslationResourceData Clean(TranslationResourceData data)
        {
            var memory = (data.Memory ?? new List<TranslationMemoryEntry>())
                .Where(IsValidMemory)
                .GroupBy(entry => Normalize(entry.Source), StringComparer.Ordinal)
                .Select(group => group.Last())
                .OrderByDescending(entry => entry.UpdatedAt)
                .ToList();
            var glossary = (data.Glossary ?? new List<GlossaryEntry>())
                .Where(IsValidGlossary)
                .GroupBy(entry => Normalize(entry.Source), StringComparer.Ordinal)
                .Select(group => group.Last())
                .OrderBy(entry => entry.Source, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            return new TranslationResourceData
            {
                Version = Math.Max(1, data.Version),
                Memory = memory,
                Glossary = glossary
            };
        }

        private static bool IsValidMemory(TranslationMemoryEntry entry)
        {
            return entry != null &&
                   !string.IsNullOrWhiteSpace(entry.Source) &&
                   !string.IsNullOrWhiteSpace(entry.Translation);
        }

        private static bool IsValidGlossary(GlossaryEntry entry)
        {
            return entry != null &&
                   !string.IsNullOrWhiteSpace(entry.Source) &&
                   !string.IsNullOrWhiteSpace(entry.Translation);
        }
    }
}
