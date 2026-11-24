using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace StudyFocus
{
    internal sealed partial class MainWindow
    {
        private interface IPolishedControl
        {
            void ApplyColors(WindowPalette colors);
        }

        private static void PaintPill(Graphics graphics, Rectangle bounds, int radius, Color fill, Color border)
        {
            if (bounds.Width < 2 || bounds.Height < 2) return;
            using (var path = RoundedPath(bounds, radius))
            using (var brush = new SolidBrush(fill))
            {
                graphics.FillPath(brush, path);
                if (!border.IsEmpty)
                    using (var pen = new Pen(border)) graphics.DrawPath(pen, path);
            }
        }

        private static Control PolishedHeading(string caption, int glyph)
        {
            var row = Columns(0, 100);
            Rows(row, -1);
            row.ColumnStyles[0] = new ColumnStyle(SizeType.Absolute, 45);
            row.Controls.Add(new PolishedGlyph(glyph, 27) { Dock = DockStyle.Fill, Margin = Padding.Empty }, 0, 0);
            row.Controls.Add(Label(caption, 13, "title"), 1, 0);
            return row;
        }

        private sealed class PolishedGlyph : Control, IPolishedControl
        {
            private readonly int glyph, size;
            private WindowPalette colors;

            public PolishedGlyph(int glyph, int size)
            {
                this.glyph = glyph;
                this.size = size;
                DoubleBuffered = ResizeRedraw = true;
            }

            void IPolishedControl.ApplyColors(WindowPalette palette) { colors = palette; Invalidate(); }

            protected override void OnPaint(PaintEventArgs e)
            {
                if (colors == null) return;
                var g = e.Graphics;
                float scale = g.DpiX / 96f;
                int d = Math.Min(Math.Min(Width, Height), (int)Math.Round(size * scale));
                if (d < 8) return;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                int x = (Width - d) / 2, y = (Height - d) / 2;
                PaintPill(g, new Rectangle(x, y, d, d), (int)(8 * scale), colors.Subtle, Color.Empty);
                using (var icon = NavGlyph(glyph, colors.Accent, scale * (size >= 40 ? 1.2f : .86f)))
                    g.DrawImageUnscaled(icon, x + (d - icon.Width) / 2, y + (d - icon.Height) / 2);
            }
        }

        private sealed class PolishedInput : Panel, IPolishedControl
        {
            private WindowPalette colors;
            public readonly TextBox Editor;

            public PolishedInput(string placeholder)
            {
                Dock = DockStyle.Fill;
                Margin = new Padding(0, 3, 0, 3);
                DoubleBuffered = true;
                ResizeRedraw = true;
                Editor = new CueEditor(placeholder) { AccessibleName = "搜索软件（名称或进程）", BorderStyle = BorderStyle.None };
                Controls.Add(Editor);
                Editor.GotFocus += delegate { Invalidate(); };
                Editor.LostFocus += delegate { Invalidate(); };
                MouseDown += delegate { Editor.Focus(); };
            }

            protected override void OnLayout(LayoutEventArgs e)
            {
                base.OnLayout(e);
                if (Editor == null) return;
                int left = (int)Math.Round(36 * DeviceDpi / 96f);
                int height = Editor.PreferredHeight;
                var bounds = new Rectangle(left, Math.Max(0, (Height - height) / 2), Math.Max(1, Width - left - 8), height);
                Editor.Bounds = bounds;
            }

            void IPolishedControl.ApplyColors(WindowPalette palette)
            {
                colors = palette;
                Editor.BackColor = colors.Field;
                ((CueEditor)Editor).CueColor = colors.Muted;
                Editor.Invalidate();
                Invalidate();
            }

            private sealed class CueEditor : TextBox
            {
                private readonly string placeholder;
                public Color CueColor { get; set; }

                public CueEditor(string placeholder) { this.placeholder = placeholder; }

                protected override void WndProc(ref Message message)
                {
                    base.WndProc(ref message);
                    const int paint = 0x000F, print = 0x0317, printClient = 0x0318;
                    if (message.Msg != paint && message.Msg != print && message.Msg != printClient) return;
                    if (CueColor.IsEmpty || Focused || TextLength != 0) return;
                    if (message.Msg == paint && IsHandleCreated)
                    {
                        using (var g = Graphics.FromHwnd(Handle)) DrawCue(g);
                    }
                    else if ((message.Msg == print || message.Msg == printClient) && message.WParam != IntPtr.Zero)
                    {
                        using (var g = Graphics.FromHdc(message.WParam)) DrawCue(g);
                    }
                }

                private void DrawCue(Graphics graphics)
                {
                    TextRenderer.DrawText(graphics, placeholder, Font, ClientRectangle, CueColor,
                        TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
                }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                if (colors == null) return;
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float dpi = g.DpiX / 96f;
                PaintPill(g, new Rectangle(0, 0, Width - 1, Height - 1), (int)(8 * dpi), colors.Field,
                    Editor.Focused ? colors.Accent : colors.Outline);
                using (var pen = new Pen(colors.Muted, 1.5f * dpi) { EndCap = LineCap.Round })
                {
                    float cx = 15 * dpi, cy = Height / 2f - dpi;
                    g.DrawEllipse(pen, cx - 5 * dpi, cy - 5 * dpi, 10 * dpi, 10 * dpi);
                    g.DrawLine(pen, cx + 4 * dpi, cy + 4 * dpi, cx + 8 * dpi, cy + 8 * dpi);
                }
            }
        }

        private sealed class PolishedSwitch : CheckBox, IPolishedControl
        {
            private WindowPalette colors;

            public PolishedSwitch(string label)
            {
                Text = label;
                Dock = DockStyle.Fill;
                Height = 40;
                Margin = new Padding(0, 1, 0, 1);
                Cursor = Cursors.Hand;
                UseVisualStyleBackColor = false;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Opaque, true);
            }

            void IPolishedControl.ApplyColors(WindowPalette palette)
            {
                colors = palette;
                Invalidate();
            }

            protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Invalidate(); }

            protected override void OnPaint(PaintEventArgs e)
            {
                using (var background = new SolidBrush(BackColor)) e.Graphics.FillRectangle(background, ClientRectangle);
                if (colors == null || SystemInformation.HighContrast) { base.OnPaint(e); return; }
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float dpi = g.DpiX / 96f;
                int w = (int)(39 * dpi), h = (int)(22 * dpi), d = h - (int)(6 * dpi);
                var track = new Rectangle(Math.Max(0, Width - w - 2), (Height - h) / 2, w, h);
                var border = Focused && ShowFocusCues ? colors.Accent : colors.Outline;
                var fill = Checked ? (Enabled ? colors.PrimaryFill : colors.Subtle) : colors.Field;
                PaintPill(g, track, h / 2 - 1, fill, border);
                using (var thumb = new SolidBrush(Checked && Enabled ? Color.White : colors.Muted))
                    g.FillEllipse(thumb, Checked ? track.Right - d - 3 : track.Left + 3, track.Top + (h - d) / 2, d, d);
                var caption = new Rectangle(2, 0, Math.Max(1, track.Left - 12), Height);
                TextRenderer.DrawText(g, Text, Font, caption, Enabled ? colors.Ink : colors.Muted,
                    TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix |
                    TextFormatFlags.PreserveGraphicsClipping | TextFormatFlags.PreserveGraphicsTranslateTransform);
                if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(ClientRectangle, -1, -1));
            }
        }

        private sealed class PolishedSlider : TrackBar, IPolishedControl
        {
            [StructLayout(LayoutKind.Sequential)]
            private struct NativeRect { public int Left, Top, Right, Bottom; }

            [DllImport("user32.dll", EntryPoint = "SendMessageW")]
            private static extern IntPtr TrackMessage(IntPtr window, int message, IntPtr parameter, ref NativeRect rect);

            private WindowPalette colors;

            public PolishedSlider()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Opaque, true);
            }

            void IPolishedControl.ApplyColors(WindowPalette palette)
            {
                colors = palette;
                Invalidate();
            }

            protected override void OnValueChanged(EventArgs e) { base.OnValueChanged(e); Invalidate(); }
            protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
            protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

            protected override void OnPaint(PaintEventArgs e)
            {
                using (var background = new SolidBrush(BackColor)) e.Graphics.FillRectangle(background, ClientRectangle);
                if (colors == null || SystemInformation.HighContrast)
                {
                    var native = e.Graphics.GetHdc();
                    try
                    {
                        Message print = Message.Create(Handle, 0x0318, native, new IntPtr(4));
                        DefWndProc(ref print);
                    }
                    finally { e.Graphics.ReleaseHdc(native); }
                    return;
                }
                var channel = new NativeRect();
                var thumb = new NativeRect();
                TrackMessage(Handle, 0x041A, IntPtr.Zero, ref channel);
                TrackMessage(Handle, 0x0419, IntPtr.Zero, ref thumb);
                int center = (thumb.Left + thumb.Right) / 2;
                int y = (thumb.Top + thumb.Bottom) / 2;
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float dpi = g.DpiX / 96f;
                using (var pen = new Pen(colors.Outline, 4 * dpi) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawLine(pen, channel.Left, y, Math.Max(channel.Left, channel.Right), y);
                using (var pen = new Pen(colors.PrimaryFill, 4 * dpi) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawLine(pen, channel.Left, y, center, y);
                using (var brush = new SolidBrush(colors.Dark ? Color.White : colors.PrimaryFill))
                    g.FillEllipse(brush, center - 6 * dpi, y - 6 * dpi, 12 * dpi, 12 * dpi);
                if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(ClientRectangle, -2, -2));
            }
        }
    }
}
