using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace GalgameUiTranslator
{
    public sealed class ImageCanvas : Control
    {
        private Bitmap _sourceImage;
        private Bitmap _previewImage;
        private Bitmap _maskOverlay;
        private ImageDocument _document;
        private TextRegion _selectedRegion;
        private float _scale = 1f;
        private PointF _offset;
        private bool _fitToWindow = true;
        private bool _creating;
        private bool _moving;
        private bool _resizing;
        private bool _panning;
        private bool _paintingMask;
        private bool _comparisonDragging;
        private float _comparisonPosition = 0.5f;
        private Point _mouseStart;
        private PointF _panStart;
        private Rectangle _originalBounds;
        private Rectangle _draftBounds;
        private RepairMaskStroke _activeMaskStroke;

        public ImageCanvas()
        {
            DoubleBuffered = true;
            BackColor = UiTheme.WindowBackground;
            TabStop = true;
            SetStyle(ControlStyles.ResizeRedraw, true);
        }

        public event EventHandler SelectionChanged;
        public event EventHandler DocumentChanged;
        public event EventHandler ZoomChanged;

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool CreateMode { get; set; }

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string DefaultFontFamily { get; set; } = "Microsoft YaHei";

        public bool PreviewEnabled { get; private set; }

        public bool MaskEditMode { get; private set; }

        public bool MaskEraseMode { get; private set; }

        public int MaskBrushSize { get; private set; } = 18;

        public bool ShowAtlasOverlay { get; private set; }

        public bool ComparisonEnabled { get; private set; }

        public float ComparisonPosition => _comparisonPosition;

        public TextRegion SelectedRegion => _selectedRegion;

        public string ZoomText => _sourceImage == null ? "" : $"{_scale * 100:0}%";

        public bool HasSelectedMask => _selectedRegion?.RepairMaskStrokes?.Count > 0;

        public void SetDocument(Bitmap image, ImageDocument document)
        {
            DisposeImages();
            _sourceImage = image;
            _document = document;
            _selectedRegion = null;
            RefreshMaskOverlay();
            ZoomToFit();
            RefreshPreview();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }

        public void ClearDocument()
        {
            DisposeImages();
            _document = null;
            _selectedRegion = null;
            RefreshMaskOverlay();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }

        public void SetPreviewEnabled(bool enabled)
        {
            PreviewEnabled = enabled;
            RefreshPreview();
            Invalidate();
        }

        public void SetComparisonEnabled(bool enabled)
        {
            ComparisonEnabled = enabled;
            _comparisonDragging = false;
            RefreshPreview();
            Cursor = Cursors.Default;
            Invalidate();
        }

        public void SetComparisonPosition(float position)
        {
            _comparisonPosition = Math.Max(0.05f, Math.Min(0.95f, position));
            Invalidate();
        }

        public void NotifyRegionChanged()
        {
            RefreshPreview();
            Invalidate();
            DocumentChanged?.Invoke(this, EventArgs.Empty);
        }

        public void SelectRegion(TextRegion region)
        {
            _selectedRegion = region;
            RefreshMaskOverlay();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }

        public void SetMaskEditMode(bool enabled, bool eraser)
        {
            MaskEditMode = enabled;
            MaskEraseMode = enabled && eraser;
            _paintingMask = false;
            _activeMaskStroke = null;
            RefreshMaskOverlay();
            Cursor = enabled ? Cursors.Cross : Cursors.Default;
            Invalidate();
        }

        public void SetMaskBrushSize(int diameter)
        {
            MaskBrushSize = Math.Max(2, Math.Min(128, diameter));
        }

        public void SetAtlasOverlay(bool enabled)
        {
            ShowAtlasOverlay = enabled;
            Invalidate();
        }

        public void ClearSelectedMask()
        {
            if (_selectedRegion == null || _selectedRegion.RepairMaskStrokes.Count == 0) return;
            _selectedRegion.RepairMaskStrokes.Clear();
            RefreshMaskOverlay();
            RefreshPreview();
            DocumentChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }

        public void DeleteSelected()
        {
            if (_document == null || _selectedRegion == null)
            {
                return;
            }

            _document.Regions.Remove(_selectedRegion);
            _selectedRegion = null;
            RefreshMaskOverlay();
            RefreshPreview();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            DocumentChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }

        public void ZoomToFit()
        {
            _fitToWindow = true;
            RecalculateFit();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.Clear(BackColor);
            if (_sourceImage == null)
            {
                using (var brush = new SolidBrush(UiTheme.TextSecondary))
                using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                {
                    e.Graphics.DrawString("打开图片文件夹开始", Font, brush, ClientRectangle, format);
                }
                return;
            }

            var displayed = PreviewEnabled && _previewImage != null ? _previewImage : _sourceImage;
            var destination = ImageToScreen(new Rectangle(0, 0, displayed.Width, displayed.Height));
            e.Graphics.InterpolationMode = _scale >= 1f
                ? InterpolationMode.NearestNeighbor
                : InterpolationMode.HighQualityBicubic;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            if (ComparisonEnabled && _previewImage != null)
            {
                e.Graphics.DrawImage(_sourceImage, destination);
                var dividerX = GetComparisonScreenX(destination);
                var state = e.Graphics.Save();
                e.Graphics.SetClip(Rectangle.FromLTRB(dividerX, destination.Top, destination.Right, destination.Bottom));
                using (var background = new SolidBrush(BackColor))
                    e.Graphics.FillRectangle(background, destination);
                e.Graphics.DrawImage(_previewImage, destination);
                e.Graphics.Restore(state);
                DrawComparisonOverlay(e.Graphics, destination, dividerX);
            }
            else
            {
                e.Graphics.DrawImage(displayed, destination);
            }

            if (!ComparisonEnabled && MaskEditMode && _maskOverlay != null)
                e.Graphics.DrawImage(_maskOverlay, destination);

            if (!ComparisonEnabled && ShowAtlasOverlay && _document?.AtlasSprites != null)
            {
                foreach (var sprite in _document.AtlasSprites)
                    DrawAtlasSprite(e.Graphics, sprite);
            }

            if (!ComparisonEnabled && _document != null)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                for (var index = 0; index < _document.Regions.Count; index++)
                {
                    DrawRegion(e.Graphics, _document.Regions[index], index + 1);
                }
            }

            if (!ComparisonEnabled && _creating && _draftBounds.Width > 0 && _draftBounds.Height > 0)
            {
                using (var pen = new Pen(Color.Cyan, 2f) { DashStyle = DashStyle.Dash })
                {
                    e.Graphics.DrawRectangle(pen, ImageToScreen(_draftBounds));
                }
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_fitToWindow)
            {
                RecalculateFit();
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (_sourceImage == null)
            {
                return;
            }

            var imagePoint = ScreenToImageF(e.Location);
            var factor = e.Delta > 0 ? 1.15f : 1f / 1.15f;
            _scale = Math.Max(0.05f, Math.Min(16f, _scale * factor));
            _offset = new PointF(
                e.X - imagePoint.X * _scale,
                e.Y - imagePoint.Y * _scale);
            _fitToWindow = false;
            ZoomChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (_sourceImage == null)
            {
                return;
            }

            _mouseStart = e.Location;
            if (e.Button == MouseButtons.Middle)
            {
                _panning = true;
                _panStart = _offset;
                Cursor = Cursors.Hand;
                return;
            }

            if (e.Button != MouseButtons.Left || _document == null)
            {
                return;
            }

            if (ComparisonEnabled && IsNearComparisonDivider(e.Location))
            {
                _comparisonDragging = true;
                Cursor = Cursors.VSplit;
                UpdateComparisonPosition(e.X);
                return;
            }
            if (ComparisonEnabled) return;

            var imagePoint = ClampToImage(ScreenToImage(e.Location));
            if (MaskEditMode)
            {
                if (_selectedRegion == null) return;
                _paintingMask = true;
                _activeMaskStroke = new RepairMaskStroke
                {
                    Eraser = MaskEraseMode,
                    Diameter = MaskBrushSize
                };
                _activeMaskStroke.Points.Add(new MaskPoint(imagePoint.X, imagePoint.Y));
                _selectedRegion.RepairMaskStrokes.Add(_activeMaskStroke);
                DrawMaskOverlaySegment(_activeMaskStroke, imagePoint, imagePoint);
                Invalidate();
                return;
            }

            if (CreateMode)
            {
                _creating = true;
                _draftBounds = new Rectangle(imagePoint.X, imagePoint.Y, 0, 0);
                return;
            }

            var hit = HitTest(imagePoint);
            if (hit != _selectedRegion)
            {
                _selectedRegion = hit;
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }

            if (_selectedRegion != null)
            {
                _originalBounds = _selectedRegion.Bounds;
                var screenBounds = ImageToScreen(_selectedRegion.Bounds);
                var handle = new Rectangle(screenBounds.Right - 10, screenBounds.Bottom - 10, 20, 20);
                _resizing = handle.Contains(e.Location);
                _moving = !_resizing;
            }

            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_sourceImage == null)
            {
                return;
            }

            if (_panning)
            {
                _offset = new PointF(
                    _panStart.X + e.X - _mouseStart.X,
                    _panStart.Y + e.Y - _mouseStart.Y);
                _fitToWindow = false;
                Invalidate();
                return;
            }

            if (_comparisonDragging)
            {
                UpdateComparisonPosition(e.X);
                return;
            }

            if (_creating)
            {
                var start = ClampToImage(ScreenToImage(_mouseStart));
                var current = ClampToImage(ScreenToImage(e.Location));
                _draftBounds = NormalizeRectangle(start, current);
                Invalidate();
                return;
            }

            if (_paintingMask && _activeMaskStroke != null)
            {
                var current = ClampToImage(ScreenToImage(e.Location));
                var lastPoint = _activeMaskStroke.Points[_activeMaskStroke.Points.Count - 1];
                var minimumStep = Math.Max(1, MaskBrushSize / 6);
                if (Math.Abs(current.X - lastPoint.X) + Math.Abs(current.Y - lastPoint.Y) >= minimumStep)
                {
                    _activeMaskStroke.Points.Add(new MaskPoint(current.X, current.Y));
                    DrawMaskOverlaySegment(_activeMaskStroke, new Point(lastPoint.X, lastPoint.Y), current);
                    Invalidate();
                }
                return;
            }

            if ((_moving || _resizing) && _selectedRegion != null)
            {
                var deltaX = (int)Math.Round((e.X - _mouseStart.X) / _scale);
                var deltaY = (int)Math.Round((e.Y - _mouseStart.Y) / _scale);
                if (_resizing)
                {
                    _selectedRegion.Width = Math.Max(4, Math.Min(
                        _sourceImage.Width - _selectedRegion.X,
                        _originalBounds.Width + deltaX));
                    _selectedRegion.Height = Math.Max(4, Math.Min(
                        _sourceImage.Height - _selectedRegion.Y,
                        _originalBounds.Height + deltaY));
                }
                else
                {
                    _selectedRegion.X = Math.Max(0, Math.Min(
                        _sourceImage.Width - _selectedRegion.Width,
                        _originalBounds.X + deltaX));
                    _selectedRegion.Y = Math.Max(0, Math.Min(
                        _sourceImage.Height - _selectedRegion.Height,
                        _originalBounds.Y + deltaY));
                }

                Invalidate();
                return;
            }

            if (ComparisonEnabled)
            {
                Cursor = IsNearComparisonDivider(e.Location) ? Cursors.VSplit : Cursors.Default;
            }
            else if (!CreateMode && _selectedRegion != null)
            {
                var bounds = ImageToScreen(_selectedRegion.Bounds);
                var handle = new Rectangle(bounds.Right - 10, bounds.Bottom - 10, 20, 20);
                Cursor = handle.Contains(e.Location) ? Cursors.SizeNWSE : Cursors.Default;
            }
            else
            {
                Cursor = CreateMode ? Cursors.Cross : Cursors.Default;
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_comparisonDragging)
            {
                _comparisonDragging = false;
                Cursor = IsNearComparisonDivider(e.Location) ? Cursors.VSplit : Cursors.Default;
                return;
            }
            if (_panning)
            {
                _panning = false;
                Cursor = Cursors.Default;
                return;
            }

            if (_paintingMask)
            {
                _paintingMask = false;
                _activeMaskStroke = null;
                RefreshPreview();
                DocumentChanged?.Invoke(this, EventArgs.Empty);
                SelectionChanged?.Invoke(this, EventArgs.Empty);
                Invalidate();
                return;
            }

            if (_creating)
            {
                _creating = false;
                if (_draftBounds.Width >= 4 && _draftBounds.Height >= 4 && _document != null)
                {
                    var region = new TextRegion
                    {
                        X = _draftBounds.X,
                        Y = _draftBounds.Y,
                        Width = _draftBounds.Width,
                        Height = _draftBounds.Height,
                        FontFamily = DefaultFontFamily,
                        FontSize = Math.Max(12f, _draftBounds.Height * 0.7f),
                        Confidence = 1f
                    };
                    _document.Regions.Add(region);
                    _selectedRegion = region;
                    SelectionChanged?.Invoke(this, EventArgs.Empty);
                    DocumentChanged?.Invoke(this, EventArgs.Empty);
                }

                _draftBounds = Rectangle.Empty;
                Invalidate();
                return;
            }

            if (_moving || _resizing)
            {
                _moving = false;
                _resizing = false;
                RefreshPreview();
                DocumentChanged?.Invoke(this, EventArgs.Empty);
                SelectionChanged?.Invoke(this, EventArgs.Empty);
                Invalidate();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeImages();
            }

            base.Dispose(disposing);
        }

        private void DrawRegion(Graphics graphics, TextRegion region, int index)
        {
            var bounds = ImageToScreen(region.Bounds);
            var color = ReferenceEquals(region, _selectedRegion)
                ? Color.Cyan
                : region.Reviewed
                    ? Color.LimeGreen
                    : region.Confidence < 0.75f ? Color.Orange : Color.DeepSkyBlue;

            using (var fill = new SolidBrush(Color.FromArgb(28, color)))
            using (var pen = new Pen(color, ReferenceEquals(region, _selectedRegion) ? 2.2f : 1.2f))
            using (var labelBrush = new SolidBrush(Color.FromArgb(210, 15, 18, 22)))
            using (var textBrush = new SolidBrush(Color.White))
            {
                graphics.FillRectangle(fill, bounds);
                graphics.DrawRectangle(pen, bounds);
                var label = index.ToString();
                var labelSize = graphics.MeasureString(label, Font);
                var labelRect = new RectangleF(bounds.Left, bounds.Top, labelSize.Width + 6, labelSize.Height + 2);
                graphics.FillRectangle(labelBrush, labelRect);
                graphics.DrawString(label, Font, textBrush, bounds.Left + 3, bounds.Top + 1);

                if (ReferenceEquals(region, _selectedRegion))
                {
                    using (var handleBrush = new SolidBrush(color))
                    {
                        graphics.FillRectangle(handleBrush, bounds.Right - 5, bounds.Bottom - 5, 10, 10);
                    }
                }
            }
        }

        private void DrawComparisonOverlay(Graphics graphics, Rectangle destination, int dividerX)
        {
            using (var shadowPen = new Pen(Color.FromArgb(150, 0, 0, 0), 5f))
            using (var linePen = new Pen(Color.FromArgb(245, 115, 190, 255), 2f))
            using (var handleBrush = new SolidBrush(Color.FromArgb(245, 115, 190, 255)))
            using (var handleText = new SolidBrush(Color.FromArgb(255, 8, 18, 32)))
            using (var handleFont = UiTheme.CreateFont(12f, FontStyle.Bold))
            {
                graphics.DrawLine(shadowPen, dividerX, destination.Top, dividerX, destination.Bottom);
                graphics.DrawLine(linePen, dividerX, destination.Top, dividerX, destination.Bottom);
                var handle = new Rectangle(dividerX - 17, destination.Top + destination.Height / 2 - 17, 34, 34);
                graphics.FillEllipse(handleBrush, handle);
                using (var format = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                })
                {
                    graphics.DrawString("↔", handleFont, handleText, handle, format);
                }
            }

            if (destination.Width >= 200 && destination.Height >= 52)
            {
                DrawComparisonLabel(graphics, "原图", destination.Left + 10, destination.Top + 10, false);
                var labelWidth = 76;
                DrawComparisonLabel(graphics, "汉化效果", destination.Right - labelWidth - 10, destination.Top + 10, true);
            }
        }

        private static void DrawComparisonLabel(Graphics graphics, string text, int x, int y, bool accent)
        {
            var bounds = new Rectangle(x, y, 76, 28);
            using (var background = new SolidBrush(accent
                       ? Color.FromArgb(225, UiTheme.Accent)
                       : Color.FromArgb(210, 10, 20, 34)))
            using (var foreground = new SolidBrush(Color.White))
            using (var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            })
            using (var font = UiTheme.CreateFont(8.5f, FontStyle.Bold))
            {
                graphics.FillRectangle(background, bounds);
                graphics.DrawString(text, font, foreground, bounds, format);
            }
        }

        private void DrawAtlasSprite(Graphics graphics, AtlasSprite sprite)
        {
            var bounds = ImageToScreen(sprite.Bounds);
            if (bounds.Width <= 0 || bounds.Height <= 0 || !graphics.VisibleClipBounds.IntersectsWith(bounds)) return;
            using (var pen = new Pen(Color.FromArgb(205, 188, 112, 255), 1.2f)
            {
                DashStyle = DashStyle.Dash
            })
            {
                graphics.DrawRectangle(pen, bounds);
            }

            if (_scale < 0.35f || bounds.Width < 34 || bounds.Height < 16) return;
            var label = sprite.Name ?? string.Empty;
            if (label.Length > 24) label = label.Substring(0, 21) + "...";
            if (sprite.Rotated) label += " ↻";
            using (var background = new SolidBrush(Color.FromArgb(190, 55, 31, 78)))
            using (var foreground = new SolidBrush(Color.FromArgb(245, 235, 220, 255)))
            {
                var size = graphics.MeasureString(label, Font);
                var labelBounds = new RectangleF(bounds.Left, bounds.Top, size.Width + 6, size.Height + 2);
                graphics.FillRectangle(background, labelBounds);
                graphics.DrawString(label, Font, foreground, bounds.Left + 3, bounds.Top + 1);
            }
        }

        private TextRegion HitTest(Point imagePoint)
        {
            if (_document == null)
            {
                return null;
            }

            for (var index = _document.Regions.Count - 1; index >= 0; index--)
            {
                if (_document.Regions[index].Bounds.Contains(imagePoint))
                {
                    return _document.Regions[index];
                }
            }

            return null;
        }

        private void RefreshPreview()
        {
            _previewImage?.Dispose();
            _previewImage = null;
            if ((PreviewEnabled || ComparisonEnabled) && _sourceImage != null && _document != null)
            {
                _previewImage = ImageProcessor.RenderPreview(_sourceImage, _document);
            }
        }

        private bool IsNearComparisonDivider(Point point)
        {
            if (!ComparisonEnabled || _sourceImage == null) return false;
            var destination = ImageToScreen(new Rectangle(0, 0, _sourceImage.Width, _sourceImage.Height));
            if (point.Y < destination.Top || point.Y > destination.Bottom) return false;
            return Math.Abs(point.X - GetComparisonScreenX(destination)) <= 18;
        }

        private int GetComparisonScreenX(Rectangle destination)
        {
            return destination.Left + (int)Math.Round(destination.Width * _comparisonPosition);
        }

        private void UpdateComparisonPosition(int screenX)
        {
            if (_sourceImage == null) return;
            var destination = ImageToScreen(new Rectangle(0, 0, _sourceImage.Width, _sourceImage.Height));
            if (destination.Width <= 0) return;
            SetComparisonPosition((screenX - destination.Left) / (float)destination.Width);
        }

        private void RefreshMaskOverlay()
        {
            _maskOverlay?.Dispose();
            _maskOverlay = null;
            if (_sourceImage == null || _selectedRegion?.RepairMaskStrokes == null) return;

            _maskOverlay = new Bitmap(_sourceImage.Width, _sourceImage.Height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(_maskOverlay))
            {
                graphics.Clear(Color.Transparent);
                foreach (var stroke in _selectedRegion.RepairMaskStrokes)
                    DrawMaskOverlayStroke(graphics, stroke);
            }
        }

        private void DrawMaskOverlaySegment(RepairMaskStroke stroke, Point first, Point second)
        {
            if (_maskOverlay == null && _sourceImage != null)
                _maskOverlay = new Bitmap(_sourceImage.Width, _sourceImage.Height, PixelFormat.Format32bppArgb);
            if (_maskOverlay == null) return;

            using (var graphics = Graphics.FromImage(_maskOverlay))
                DrawMaskOverlaySegment(graphics, stroke, first, second);
        }

        private static void DrawMaskOverlayStroke(Graphics graphics, RepairMaskStroke stroke)
        {
            if (stroke?.Points == null || stroke.Points.Count == 0) return;
            var first = new Point(stroke.Points[0].X, stroke.Points[0].Y);
            DrawMaskOverlaySegment(graphics, stroke, first, first);
            for (var index = 1; index < stroke.Points.Count; index++)
            {
                var second = new Point(stroke.Points[index].X, stroke.Points[index].Y);
                DrawMaskOverlaySegment(graphics, stroke, first, second);
                first = second;
            }
        }

        private static void DrawMaskOverlaySegment(
            Graphics graphics,
            RepairMaskStroke stroke,
            Point first,
            Point second)
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.CompositingMode = CompositingMode.SourceCopy;
            var color = stroke.Eraser ? Color.Transparent : Color.FromArgb(125, 255, 45, 125);
            var diameter = Math.Max(2, stroke.Diameter);
            using (var pen = new Pen(color, diameter)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            })
            {
                graphics.DrawLine(pen, first, second);
            }
        }

        private void RecalculateFit()
        {
            if (_sourceImage == null || ClientSize.Width <= 0 || ClientSize.Height <= 0)
            {
                return;
            }

            var horizontal = (ClientSize.Width - 32f) / _sourceImage.Width;
            var vertical = (ClientSize.Height - 32f) / _sourceImage.Height;
            _scale = Math.Max(0.05f, Math.Min(horizontal, vertical));
            _offset = new PointF(
                (ClientSize.Width - _sourceImage.Width * _scale) / 2f,
                (ClientSize.Height - _sourceImage.Height * _scale) / 2f);
            ZoomChanged?.Invoke(this, EventArgs.Empty);
        }

        private Rectangle ImageToScreen(Rectangle rectangle)
        {
            return new Rectangle(
                (int)Math.Round(_offset.X + rectangle.X * _scale),
                (int)Math.Round(_offset.Y + rectangle.Y * _scale),
                Math.Max(1, (int)Math.Round(rectangle.Width * _scale)),
                Math.Max(1, (int)Math.Round(rectangle.Height * _scale)));
        }

        private Point ScreenToImage(Point point)
        {
            var value = ScreenToImageF(point);
            return new Point((int)Math.Round(value.X), (int)Math.Round(value.Y));
        }

        private PointF ScreenToImageF(Point point)
        {
            return new PointF(
                (point.X - _offset.X) / _scale,
                (point.Y - _offset.Y) / _scale);
        }

        private Point ClampToImage(Point point)
        {
            return new Point(
                Math.Max(0, Math.Min((_sourceImage?.Width ?? 1) - 1, point.X)),
                Math.Max(0, Math.Min((_sourceImage?.Height ?? 1) - 1, point.Y)));
        }

        private static Rectangle NormalizeRectangle(Point first, Point second)
        {
            return Rectangle.FromLTRB(
                Math.Min(first.X, second.X),
                Math.Min(first.Y, second.Y),
                Math.Max(first.X, second.X),
                Math.Max(first.Y, second.Y));
        }

        private void DisposeImages()
        {
            _sourceImage?.Dispose();
            _sourceImage = null;
            _previewImage?.Dispose();
            _previewImage = null;
            _maskOverlay?.Dispose();
            _maskOverlay = null;
        }
    }
}
