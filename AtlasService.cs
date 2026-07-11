using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace GalgameUiTranslator
{
    public static class AtlasService
    {
        private static readonly HashSet<string> ImageExtensions = new HashSet<string>(
            new[] { ".png", ".jpg", ".jpeg", ".bmp", ".dds" },
            StringComparer.OrdinalIgnoreCase);

        public static int RefreshProject(TranslationProject project)
        {
            if (project == null || string.IsNullOrWhiteSpace(project.SourceFolder) ||
                !Directory.Exists(project.SourceFolder)) return 0;

            var attached = 0;
            foreach (var path in Directory.EnumerateFiles(project.SourceFolder, "*.json", SearchOption.AllDirectories))
            {
                try { attached += ParseTexturePackerJson(project, path); }
                catch { }
            }
            foreach (var path in Directory.EnumerateFiles(project.SourceFolder, "*.atlas", SearchOption.AllDirectories))
            {
                try { attached += ParseSpineAtlas(project, path); }
                catch { }
            }
            return attached;
        }

        private static int ParseTexturePackerJson(TranslationProject project, string metadataPath)
        {
            using (var json = JsonDocument.Parse(File.ReadAllText(metadataPath)))
            {
                var root = json.RootElement;
                if (!TryGet(root, "frames", out var frames) ||
                    (frames.ValueKind != JsonValueKind.Object && frames.ValueKind != JsonValueKind.Array)) return 0;

                var imageName = string.Empty;
                if (TryGet(root, "meta", out var meta) && TryGet(meta, "image", out var image) &&
                    image.ValueKind == JsonValueKind.String)
                    imageName = image.GetString() ?? string.Empty;
                var document = FindDocument(project, metadataPath, imageName);
                if (document == null) return 0;

                var sprites = new List<AtlasSprite>();
                if (frames.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in frames.EnumerateObject())
                    {
                        var sprite = ReadJsonSprite(property.Name, property.Value);
                        if (sprite != null) sprites.Add(sprite);
                    }
                }
                else
                {
                    foreach (var item in frames.EnumerateArray())
                    {
                        var name = ReadString(item, "filename", ReadString(item, "name", string.Empty));
                        var sprite = ReadJsonSprite(name, item);
                        if (sprite != null) sprites.Add(sprite);
                    }
                }

                return Attach(project, document, metadataPath, sprites);
            }
        }

        private static AtlasSprite ReadJsonSprite(string name, JsonElement item)
        {
            var frame = item;
            if (TryGet(item, "frame", out var nested)) frame = nested;
            var x = ReadInt(frame, "x", -1);
            var y = ReadInt(frame, "y", -1);
            var width = ReadInt(frame, "w", ReadInt(frame, "width", -1));
            var height = ReadInt(frame, "h", ReadInt(frame, "height", -1));
            if (x < 0 || y < 0 || width <= 0 || height <= 0) return null;
            return new AtlasSprite
            {
                Name = string.IsNullOrWhiteSpace(name) ? "sprite" : name,
                X = x,
                Y = y,
                Width = width,
                Height = height,
                Rotated = ReadBool(item, "rotated")
            };
        }

        private static int ParseSpineAtlas(TranslationProject project, string metadataPath)
        {
            var lines = File.ReadAllLines(metadataPath);
            ImageDocument currentPage = null;
            var attached = 0;
            for (var index = 0; index < lines.Length; index++)
            {
                var raw = lines[index];
                var value = raw.Trim();
                if (value.Length == 0) continue;
                if (raw.Length != value.Length || value.Contains(":")) continue;

                if (ImageExtensions.Contains(Path.GetExtension(value)))
                {
                    currentPage = FindDocument(project, metadataPath, value);
                    continue;
                }
                if (currentPage == null) continue;

                var sprite = new AtlasSprite { Name = value };
                var foundBounds = false;
                var next = index + 1;
                while (next < lines.Length)
                {
                    var propertyRaw = lines[next];
                    if (propertyRaw.Trim().Length == 0) break;
                    if (propertyRaw.Length == propertyRaw.Trim().Length && !propertyRaw.Contains(":")) break;
                    var property = propertyRaw.Trim();
                    var separator = property.IndexOf(':');
                    if (separator > 0)
                    {
                        var key = property.Substring(0, separator).Trim();
                        var numbers = ParseNumbers(property.Substring(separator + 1));
                        if (key.Equals("bounds", StringComparison.OrdinalIgnoreCase) && numbers.Length >= 4)
                        {
                            sprite.X = numbers[0]; sprite.Y = numbers[1];
                            sprite.Width = numbers[2]; sprite.Height = numbers[3];
                            foundBounds = true;
                        }
                        else if (key.Equals("xy", StringComparison.OrdinalIgnoreCase) && numbers.Length >= 2)
                        {
                            sprite.X = numbers[0]; sprite.Y = numbers[1];
                        }
                        else if (key.Equals("size", StringComparison.OrdinalIgnoreCase) && numbers.Length >= 2)
                        {
                            sprite.Width = numbers[0]; sprite.Height = numbers[1];
                            foundBounds = true;
                        }
                        else if (key.Equals("rotate", StringComparison.OrdinalIgnoreCase))
                        {
                            var text = property.Substring(separator + 1).Trim();
                            sprite.Rotated = text.Equals("true", StringComparison.OrdinalIgnoreCase) || text == "90";
                        }
                    }
                    next++;
                }

                if (foundBounds && sprite.Width > 0 && sprite.Height > 0)
                    attached += Attach(project, currentPage, metadataPath, new[] { sprite }, false);
                index = Math.Max(index, next - 1);
            }
            return attached;
        }

        private static int Attach(
            TranslationProject project,
            ImageDocument document,
            string metadataPath,
            IEnumerable<AtlasSprite> sprites,
            bool replace = true)
        {
            var incoming = sprites.Where(sprite => sprite.Width > 0 && sprite.Height > 0).ToList();
            if (incoming.Count == 0) return 0;
            if (replace) document.AtlasSprites.Clear();
            foreach (var sprite in incoming)
            {
                if (document.AtlasSprites.Any(existing =>
                        existing.Name == sprite.Name && existing.Bounds == sprite.Bounds)) continue;
                document.AtlasSprites.Add(sprite);
            }
            document.AtlasMetadataPath = ProjectService.GetRelativePath(project.SourceFolder, metadataPath);
            return incoming.Count;
        }

        private static ImageDocument FindDocument(
            TranslationProject project,
            string metadataPath,
            string imageName)
        {
            var metadataDirectory = Path.GetDirectoryName(metadataPath) ?? project.SourceFolder;
            if (!string.IsNullOrWhiteSpace(imageName))
            {
                var fullPath = Path.GetFullPath(Path.Combine(metadataDirectory, imageName.Replace('/', Path.DirectorySeparatorChar)));
                var relative = Normalize(ProjectService.GetRelativePath(project.SourceFolder, fullPath));
                var exact = project.Images.FirstOrDefault(image => Normalize(image.RelativePath) == relative);
                if (exact != null) return exact;
            }

            var stem = Path.GetFileNameWithoutExtension(metadataPath);
            var candidates = project.Images.Where(image =>
                Path.GetFileNameWithoutExtension(image.RelativePath).Equals(stem, StringComparison.OrdinalIgnoreCase)).ToList();
            return candidates.Count == 1 ? candidates[0] : null;
        }

        private static string Normalize(string path)
        {
            return (path ?? string.Empty).Replace('\\', '/').TrimStart('/').ToLowerInvariant();
        }

        private static bool TryGet(JsonElement element, string name, out JsonElement value)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    {
                        value = property.Value;
                        return true;
                    }
                }
            }
            value = default;
            return false;
        }

        private static string ReadString(JsonElement element, string name, string fallback)
        {
            return TryGet(element, name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? fallback
                : fallback;
        }

        private static int ReadInt(JsonElement element, string name, int fallback)
        {
            if (!TryGet(element, name, out var value)) return fallback;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)) return number;
            return value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number)
                ? number
                : fallback;
        }

        private static bool ReadBool(JsonElement element, string name)
        {
            if (!TryGet(element, name, out var value)) return false;
            if (value.ValueKind == JsonValueKind.True) return true;
            return value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out var result) && result;
        }

        private static int[] ParseNumbers(string value)
        {
            return value.Split(',')
                .Select(part => int.TryParse(part.Trim(), out var number) ? (int?)number : null)
                .Where(number => number.HasValue)
                .Select(number => number.Value)
                .ToArray();
        }
    }
}
