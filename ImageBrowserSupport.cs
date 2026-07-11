using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace GalgameUiTranslator
{
    public enum ImageWorkflowStatus
    {
        All,
        Unrecognized,
        NeedsTranslation,
        NeedsReview,
        Reviewed
    }

    public static class ImageWorkflowClassifier
    {
        public static ImageWorkflowStatus Classify(ImageDocument document)
        {
            if (document == null || document.Regions.Count == 0)
                return ImageWorkflowStatus.Unrecognized;
            if (document.Regions.Any(region => string.IsNullOrWhiteSpace(region.Translation)))
                return ImageWorkflowStatus.NeedsTranslation;
            if (document.Regions.Any(region => !region.Reviewed))
                return ImageWorkflowStatus.NeedsReview;
            return ImageWorkflowStatus.Reviewed;
        }

        public static string GetText(ImageWorkflowStatus status)
        {
            return status == ImageWorkflowStatus.Unrecognized ? "未识别"
                : status == ImageWorkflowStatus.NeedsTranslation ? "待翻译"
                : status == ImageWorkflowStatus.NeedsReview ? "待校对"
                : status == ImageWorkflowStatus.Reviewed ? "已校对"
                : "全部";
        }
    }

    public sealed class ImageThumbnailCache : IDisposable
    {
        private readonly Dictionary<string, Bitmap> _thumbnails =
            new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _loading =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _failed =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly object _sync = new object();
        private int _generation;
        private bool _disposed;

        public event EventHandler ThumbnailAvailable;

        public Bitmap GetOrQueue(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            int generation;
            lock (_sync)
            {
                if (_disposed) return null;
                if (_thumbnails.TryGetValue(path, out var thumbnail)) return thumbnail;
                if (_loading.Contains(path) || _failed.Contains(path)) return null;
                _loading.Add(path);
                generation = _generation;
            }

            Task.Run(() => LoadThumbnail(path, generation));
            return null;
        }

        public void Clear()
        {
            lock (_sync)
            {
                _generation++;
                foreach (var thumbnail in _thumbnails.Values) thumbnail.Dispose();
                _thumbnails.Clear();
                _loading.Clear();
                _failed.Clear();
            }
        }

        public static Bitmap CreateThumbnail(string path, int maximumWidth = 144, int maximumHeight = 88)
        {
            if (maximumWidth <= 0) throw new ArgumentOutOfRangeException(nameof(maximumWidth));
            if (maximumHeight <= 0) throw new ArgumentOutOfRangeException(nameof(maximumHeight));
            using (var source = ImageProcessor.LoadBitmapUnlocked(path))
            {
                var scale = Math.Min(maximumWidth / (double)source.Width, maximumHeight / (double)source.Height);
                scale = Math.Min(1d, scale);
                var width = Math.Max(1, (int)Math.Round(source.Width * scale));
                var height = Math.Max(1, (int)Math.Round(source.Height * scale));
                var thumbnail = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                using (var graphics = Graphics.FromImage(thumbnail))
                {
                    graphics.CompositingMode = CompositingMode.SourceCopy;
                    graphics.CompositingQuality = CompositingQuality.HighQuality;
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    graphics.DrawImage(source, new Rectangle(0, 0, width, height));
                }
                return thumbnail;
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;
            }
            Clear();
        }

        private void LoadThumbnail(string path, int generation)
        {
            Bitmap thumbnail = null;
            try
            {
                if (!File.Exists(path)) throw new FileNotFoundException("图片不存在。", path);
                thumbnail = CreateThumbnail(path);
            }
            catch
            {
                // Broken images keep a neutral placeholder in the list.
            }

            var notify = false;
            lock (_sync)
            {
                if (_disposed || generation != _generation)
                {
                    thumbnail?.Dispose();
                    return;
                }

                _loading.Remove(path);
                if (thumbnail == null)
                    _failed.Add(path);
                else
                    _thumbnails[path] = thumbnail;
                notify = true;
            }

            if (notify) ThumbnailAvailable?.Invoke(this, EventArgs.Empty);
        }
    }
}
