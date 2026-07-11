using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace GalgameUiTranslator
{
    public static class UiTheme
    {
        public static readonly Color WindowBackground = Color.FromArgb(9, 16, 27);
        public static readonly Color SidebarBackground = Color.FromArgb(16, 27, 42);
        public static readonly Color CardBackground = Color.FromArgb(21, 33, 50);
        public static readonly Color CardBackgroundLight = Color.FromArgb(25, 40, 61);
        public static readonly Color InputBackground = Color.FromArgb(12, 23, 37);
        public static readonly Color Border = Color.FromArgb(48, 72, 101);
        public static readonly Color BorderSoft = Color.FromArgb(37, 55, 77);
        public static readonly Color Accent = Color.FromArgb(94, 145, 242);
        public static readonly Color AccentHover = Color.FromArgb(112, 160, 250);
        public static readonly Color AccentDark = Color.FromArgb(40, 66, 105);
        public static readonly Color TextPrimary = Color.FromArgb(231, 238, 248);
        public static readonly Color TextSecondary = Color.FromArgb(158, 176, 199);
        public static readonly Color Success = Color.FromArgb(62, 207, 142);
        public static readonly Color Warning = Color.FromArgb(246, 174, 75);

        public static Font CreateFont(float size, FontStyle style = FontStyle.Regular)
        {
            try
            {
                return new Font("Microsoft YaHei UI", size, style, GraphicsUnit.Point);
            }
            catch
            {
                return new Font(SystemFonts.MessageBoxFont.FontFamily, size, style, GraphicsUnit.Point);
            }
        }

        public static void Apply(Control root)
        {
            root.Font = CreateFont(root.Font.Size <= 0 ? 9f : root.Font.Size, root.Font.Style);
            ApplyRecursive(root);
        }

        private static void ApplyRecursive(Control control)
        {
            if (!(control is ImageCanvas) && !(control is CardPanel) && !(control is NavButton))
            {
                control.ForeColor = TextPrimary;
            }

            switch (control)
            {
                case Form form:
                    form.BackColor = WindowBackground;
                    break;
                case TextBox textBox:
                    textBox.BackColor = InputBackground;
                    textBox.ForeColor = TextPrimary;
                    textBox.BorderStyle = BorderStyle.FixedSingle;
                    break;
                case ListBox listBox:
                    listBox.BackColor = InputBackground;
                    listBox.ForeColor = TextPrimary;
                    listBox.BorderStyle = BorderStyle.None;
                    break;
                case ComboBox comboBox:
                    comboBox.BackColor = InputBackground;
                    comboBox.ForeColor = TextPrimary;
                    comboBox.FlatStyle = FlatStyle.Flat;
                    break;
                case NumericUpDown numeric:
                    numeric.BackColor = InputBackground;
                    numeric.ForeColor = TextPrimary;
                    numeric.BorderStyle = BorderStyle.FixedSingle;
                    break;
                case GroupBox groupBox:
                    groupBox.ForeColor = TextPrimary;
                    groupBox.BackColor = CardBackground;
                    break;
                case Button button when !(button is NavButton):
                    button.FlatStyle = FlatStyle.Flat;
                    button.FlatAppearance.BorderColor = Border;
                    button.FlatAppearance.MouseOverBackColor = AccentDark;
                    button.FlatAppearance.MouseDownBackColor = Color.FromArgb(34, 58, 94);
                    button.BackColor = button.Tag is Color ? button.BackColor : CardBackgroundLight;
                    button.ForeColor = button.Tag is Color ? button.ForeColor : TextPrimary;
                    break;
                case CheckBox checkBox:
                    checkBox.ForeColor = TextPrimary;
                    break;
                case RadioButton radioButton:
                    radioButton.ForeColor = TextPrimary;
                    break;
                case TableLayoutPanel table:
                    if (table.BackColor == SystemColors.Control || table.BackColor == Color.Transparent)
                        table.BackColor = Color.Transparent;
                    break;
                case FlowLayoutPanel flow:
                    if (flow.BackColor == SystemColors.Control || flow.BackColor == Color.Transparent)
                        flow.BackColor = Color.Transparent;
                    break;
                case Panel panel:
                    if (panel.BackColor == SystemColors.Control)
                        panel.BackColor = Color.Transparent;
                    break;
                case Label label:
                    if (label.ForeColor == SystemColors.ControlText || label.ForeColor == Color.Black)
                        label.ForeColor = TextPrimary;
                    break;
            }

            foreach (Control child in control.Controls)
            {
                child.Font = CreateFont(child.Font.Size <= 0 ? 9f : child.Font.Size, child.Font.Style);
                ApplyRecursive(child);
            }
        }
    }

    public sealed class CardPanel : Panel
    {
        public CardPanel()
        {
            DoubleBuffered = true;
            BackColor = UiTheme.CardBackground;
            Padding = new Padding(18);
        }

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int CornerRadius { get; set; } = 22;

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color BorderColor { get; set; } = UiTheme.Border;

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int BorderWidth { get; set; } = 1;

        protected override void OnResize(EventArgs eventArgs)
        {
            base.OnResize(eventArgs);
            UpdateRegion();
        }

        protected override void OnPaint(PaintEventArgs eventArgs)
        {
            base.OnPaint(eventArgs);
            if (Width <= 1 || Height <= 1) return;
            eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = CreateRoundedRectangle(
                       new Rectangle(0, 0, Width - 1, Height - 1),
                       CornerRadius))
            using (var pen = new Pen(BorderColor, BorderWidth))
            {
                eventArgs.Graphics.DrawPath(pen, path);
            }
        }

        private void UpdateRegion()
        {
            if (Width <= 1 || Height <= 1) return;
            using (var path = CreateRoundedRectangle(new Rectangle(0, 0, Width, Height), CornerRadius))
            {
                Region = new Region(path);
            }
        }

        internal static GraphicsPath CreateRoundedRectangle(Rectangle rectangle, int radius)
        {
            var path = new GraphicsPath();
            var diameter = Math.Max(2, Math.Min(Math.Min(rectangle.Width, rectangle.Height), radius * 2));
            var arc = new Rectangle(rectangle.Location, new Size(diameter, diameter));
            path.AddArc(arc, 180, 90);
            arc.X = rectangle.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = rectangle.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = rectangle.Left;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    public class ModernButton : Button
    {
        private bool _hovered;

        public ModernButton()
        {
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 1;
            FlatAppearance.BorderColor = UiTheme.Border;
            BackColor = UiTheme.CardBackgroundLight;
            ForeColor = UiTheme.TextPrimary;
            Cursor = Cursors.Hand;
            Height = 42;
            Padding = new Padding(12, 0, 12, 0);
            Font = UiTheme.CreateFont(10f, FontStyle.Bold);
        }

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool AccentStyle { get; set; }

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int CornerRadius { get; set; } = 12;

        protected override void OnMouseEnter(EventArgs eventArgs)
        {
            _hovered = true;
            Invalidate();
            base.OnMouseEnter(eventArgs);
        }

        protected override void OnMouseLeave(EventArgs eventArgs)
        {
            _hovered = false;
            Invalidate();
            base.OnMouseLeave(eventArgs);
        }

        protected override void OnResize(EventArgs eventArgs)
        {
            base.OnResize(eventArgs);
            if (Width <= 1 || Height <= 1) return;
            using (var path = CardPanel.CreateRoundedRectangle(new Rectangle(0, 0, Width, Height), CornerRadius))
                Region = new Region(path);
        }

        protected override void OnPaint(PaintEventArgs eventArgs)
        {
            var fill = !Enabled
                ? UiTheme.CardBackground
                : AccentStyle
                    ? (_hovered ? UiTheme.AccentHover : UiTheme.Accent)
                    : (_hovered ? UiTheme.AccentDark : UiTheme.CardBackgroundLight);
            var border = !Enabled
                ? UiTheme.BorderSoft
                : AccentStyle ? UiTheme.Accent : UiTheme.Border;
            var textColor = Enabled ? ForeColor : UiTheme.TextSecondary;
            eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = CardPanel.CreateRoundedRectangle(new Rectangle(0, 0, Width - 1, Height - 1), CornerRadius))
            using (var brush = new SolidBrush(fill))
            using (var pen = new Pen(border))
            {
                eventArgs.Graphics.FillPath(brush, path);
                eventArgs.Graphics.DrawPath(pen, path);
            }

            TextRenderer.DrawText(
                eventArgs.Graphics,
                Text,
                Font,
                ClientRectangle,
                textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    public sealed class NavButton : Button
    {
        private bool _active;
        private bool _hovered;

        public NavButton()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.Opaque |
                ControlStyles.ResizeRedraw,
                true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = UiTheme.SidebarBackground;
            ForeColor = UiTheme.TextSecondary;
            TextAlign = ContentAlignment.MiddleLeft;
            Padding = new Padding(24, 0, 8, 0);
            Height = 54;
            Cursor = Cursors.Hand;
            Font = UiTheme.CreateFont(10f, FontStyle.Bold);
        }

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool Active
        {
            get => _active;
            set
            {
                if (_active == value) return;
                _active = value;
                Invalidate();
            }
        }

        protected override void OnMouseEnter(EventArgs eventArgs)
        {
            _hovered = true;
            Invalidate();
            base.OnMouseEnter(eventArgs);
        }

        protected override void OnMouseLeave(EventArgs eventArgs)
        {
            _hovered = false;
            Invalidate();
            base.OnMouseLeave(eventArgs);
        }

        protected override void OnPaint(PaintEventArgs eventArgs)
        {
            eventArgs.Graphics.Clear(BackColor);
            eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var bounds = new Rectangle(8, 3, Width - 16, Height - 6);
            if (_active || _hovered)
            {
                using (var path = CardPanel.CreateRoundedRectangle(bounds, 13))
                using (var brush = new SolidBrush(_active ? UiTheme.AccentDark : UiTheme.CardBackgroundLight))
                {
                    eventArgs.Graphics.FillPath(brush, path);
                }
            }

            if (_active)
            {
                using (var brush = new SolidBrush(UiTheme.Accent))
                    eventArgs.Graphics.FillRectangle(brush, 8, 16, 3, Height - 32);
            }

            TextRenderer.DrawText(
                eventArgs.Graphics,
                Text,
                Font,
                new Rectangle(Padding.Left, 0, Width - Padding.Horizontal, Height),
                Enabled
                    ? (_active ? UiTheme.TextPrimary : UiTheme.TextSecondary)
                    : Color.FromArgb(102, 119, 140),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }
    }

    public sealed class DarkToolStripRenderer : ToolStripProfessionalRenderer
    {
        public DarkToolStripRenderer() : base(new DarkColorTable())
        {
            RoundedEdges = false;
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs eventArgs)
        {
            eventArgs.TextColor = eventArgs.Item.Selected ? UiTheme.TextPrimary : UiTheme.TextSecondary;
            base.OnRenderItemText(eventArgs);
        }

        private sealed class DarkColorTable : ProfessionalColorTable
        {
            public override Color ToolStripGradientBegin => UiTheme.CardBackground;
            public override Color ToolStripGradientMiddle => UiTheme.CardBackground;
            public override Color ToolStripGradientEnd => UiTheme.CardBackground;
            public override Color ToolStripBorder => UiTheme.BorderSoft;
            public override Color SeparatorDark => UiTheme.Border;
            public override Color SeparatorLight => UiTheme.BorderSoft;
            public override Color ButtonSelectedHighlight => UiTheme.AccentDark;
            public override Color ButtonSelectedGradientBegin => UiTheme.AccentDark;
            public override Color ButtonSelectedGradientMiddle => UiTheme.AccentDark;
            public override Color ButtonSelectedGradientEnd => UiTheme.AccentDark;
            public override Color ButtonPressedGradientBegin => UiTheme.AccentDark;
            public override Color ButtonPressedGradientMiddle => UiTheme.AccentDark;
            public override Color ButtonPressedGradientEnd => UiTheme.AccentDark;
            public override Color OverflowButtonGradientBegin => UiTheme.CardBackground;
            public override Color OverflowButtonGradientMiddle => UiTheme.CardBackground;
            public override Color OverflowButtonGradientEnd => UiTheme.CardBackground;
        }
    }
}
