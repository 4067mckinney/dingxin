using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace StudyFocus
{
    internal sealed partial class MainWindow
    {
        private Label checkApps, checkSites, checkEdge, checkSchedule, checkMode, checkPause, checkStops, preflightStatus;
        private Button preflightStart, preflightEntry;
        private ModeChoice recordScope;
        private Label monthStops, monthRemainingStops, latestStop;
        private DateTime displayedRecordDay;

        private IEnumerable<StopRecord> StopsInRange(DateTime from, DateTime until)
        {
            long lower = new DateTimeOffset(from).UtcTicks, upper = new DateTimeOffset(until).UtcTicks;
            return engine.State.Stops.Where(entry => entry.WhenUtc >= lower && entry.WhenUtc < upper);
        }

        private void ExportStopRecords()
        {
            ExportStops(records.Items.Cast<ListViewItem>().Select(item => (StopRecord)item.Tag).ToArray());
        }

        private void ExportStops(IEnumerable<StopRecord> entries)
        {
            var rows = entries.Select(stop => new[] { FocusHistory.Local(stop.WhenUtc).ToString("yyyy-MM-dd HH:mm:ss"),
                stop.Minutes.ToString(), stop.Reason }).ToArray();
            ExportCsv("停止记录", new[] { "时间", "剩余分钟", "理由" }, rows);
        }

        private void ExportCsv(string name, string[] headers, IEnumerable<string[]> rows)
        {
            using (var dialog = new SaveFileDialog { Title = "导出 CSV", Filter = "CSV 文件 (*.csv)|*.csv",
                DefaultExt = "csv", FileName = name + "-" + DateTime.Today.ToString("yyyyMMdd") + ".csv" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                using (var writer = new StreamWriter(dialog.FileName, false, new UTF8Encoding(true)))
                {
                    writer.WriteLine(string.Join(",", headers.Select(CsvField)));
                    foreach (string[] row in rows) writer.WriteLine(string.Join(",", row.Select(CsvField)));
                }
            }
        }

        private static string CsvField(string value)
        {
            value = value ?? "";
            string trimmed = value.TrimStart();
            if ((value.Length > 0 && "\t\r\n".IndexOf(value[0]) >= 0) ||
                (trimmed.Length > 0 && "=+-@＝＋－＠".IndexOf(trimmed[0]) >= 0)) value = "'" + value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private static Control CheckDatum(string caption, int glyph, out Label value)
        {
            var surface = ContextCard(1, -1);
            surface.Padding = new Padding(8, 2, 9, 2);
            surface.Margin = new Padding(0, 4, 0, 4);
            var row = Columns(49, 51);
            Rows(row, -1);
            row.Controls.Add(IconHeading(caption, glyph, 10), 0, 0);
            value = Label("", 10, "muted");
            value.TextAlign = ContentAlignment.MiddleRight;
            row.Controls.Add(value, 1, 0);
            surface.Controls.Add(row, 0, 0);
            return surface;
        }

        private void RefreshPreflight()
        {
            checkApps.Text = allowedAppCount == 0 ? "仅内置系统项" : allowedAppCount + " 个软件组";
            checkSites.Text = engine.Settings.Websites.Count == 0 ? "尚未添加" : engine.Settings.Websites.Count + " 个域名";
            checkEdge.Text = ProcessGuard.FindEdge() == null ? "未检测到" : "已检测到";
            var now = DateTimeOffset.Now;
            RenderPreflight(now, Locked(now), engine.NextSchedule(now), engine.PauseRemaining(now), engine.StopsRemaining(now));
        }

        private void RenderPreflight(DateTimeOffset now, bool locked, DateTimeOffset? next, TimeSpan pauseTime, int stops)
        {
            checkSchedule.Text = next.HasValue ? DateLabel(next.Value, now) : "未设置";
            checkMode.Text = engine.Settings.CountUp ? "正计时" : engine.Settings.Minutes + " 分钟倒计时";
            checkPause.Text = DisplayDuration(pauseTime.Ticks, true);
            checkStops.Text = stops + " 次";
            preflightStatus.Text = locked ? "当前规则已锁定" : "当前配置可以开始专注";
        }
        private static Control IconHeading(string caption, int glyph, float fontSize = 13)
        {
            var row = Columns(0, 100);
            Rows(row, -1);
            row.ColumnStyles[0] = new ColumnStyle(SizeType.Absolute, fontSize >= 20 ? 42 : 31);
            row.MinimumSize = new Size(0, fontSize >= 20 ? 42 : 32);
            row.Controls.Add(new ScreenGlyph(glyph, fontSize >= 20 ? 1.45f : 1f), 0, 0);
            row.Controls.Add(Label(caption, fontSize, "title"), 1, 0);
            return row;
        }

        private sealed class StatusTag : Label
        {
            protected override void OnPaint(PaintEventArgs e)
            {
                if (Text.Length == 0) return;
                var graphics = e.Graphics;
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                int height = Font.Height + 12;
                int width = TextRenderer.MeasureText(Text, Font).Width + 32;
                var bounds = new Rectangle(Math.Max(0, Width - width - 1), (Height - height) / 2, width, height);
                using (var path = RoundedPath(bounds, height / 2 - 1))
                using (var fill = new SolidBrush(Motion.Blend(BackColor, ForeColor, .10f)))
                using (var border = new Pen(Motion.Blend(BackColor, ForeColor, .29f)))
                using (var dot = new SolidBrush(ForeColor))
                {
                    graphics.FillPath(fill, path);
                    graphics.DrawPath(border, path);
                    graphics.FillEllipse(dot, bounds.Left + 10, bounds.Top + height / 2 - 3, 6, 6);
                }
                bounds.X += 11;
                bounds.Width -= 11;
                TextRenderer.DrawText(graphics, Text, Font, bounds, ForeColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }

        private static Control BrandHeading(bool subtitle)
        {
            var row = Columns(0, 100);
            Rows(row, -1);
            row.ColumnStyles[0] = new ColumnStyle(SizeType.Absolute, subtitle ? 52 : 39);
            row.Controls.Add(new BrandBadge(subtitle ? 41 : 31), 0, 0);
            if (!subtitle)
            {
                row.Controls.Add(Label(Program.Name, 13, "title"), 1, 0);
                return row;
            }
            var words = Area(2);
            words.Margin = new Padding(5, 9, 0, 12);
            Rows(words, -62, -38);
            words.Controls.Add(Label(Program.Name, 17, "title"), 0, 0);
            words.Controls.Add(Label("专注计时", 8.5f, "muted"), 0, 1);
            row.Controls.Add(words, 1, 0);
            return row;
        }

        private sealed class BrandBadge : Control, IPolishedControl
        {
            private readonly int size;
            private WindowPalette colors;

            public BrandBadge(int size)
            {
                this.size = size;
                Dock = DockStyle.Fill;
                Margin = Padding.Empty;
                DoubleBuffered = ResizeRedraw = true;
            }

            void IPolishedControl.ApplyColors(WindowPalette palette) { colors = palette; Invalidate(); }

            protected override void OnPaint(PaintEventArgs e)
            {
                if (colors == null) return;
                int side = Math.Min(Math.Min(Width, Height), (int)Math.Round(size * e.Graphics.DpiX / 96f));
                BrandArtwork.DrawMark(e.Graphics, new Rectangle((Width - side) / 2, (Height - side) / 2, side, side),
                    Motion.Blend(colors.PrimaryFill, Color.FromArgb(11, 25, 48), .50f), Motion.Blend(colors.Accent, Color.White, .54f));
            }
        }

        private sealed class SideHorizon : Control, IPolishedControl
        {
            private WindowPalette colors;
            public SideHorizon() { DoubleBuffered = true; ResizeRedraw = true; }

            void IPolishedControl.ApplyColors(WindowPalette palette) { colors = palette; Invalidate(); }

            protected override void OnPaint(PaintEventArgs e)
            {
                if (colors == null || SystemInformation.HighContrast || Height < 78 || Width < 24) return;
                var graphics = e.Graphics;
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                var state = graphics.Save();
                float height = Math.Min(148, Height);
                graphics.TranslateTransform(0, Height - height);
                graphics.ScaleTransform(Width / 150f, height / 140f);
                Color accent = colors.Accent;

                BrandArtwork.DrawWhale(graphics, new RectangleF(6, 62, 138, 68), Color.FromArgb(colors.Dark ? 64 : 40, accent), Color.FromArgb(colors.Dark ? 160 : 110, accent));
                using (var dot = new SolidBrush(Color.FromArgb(colors.Dark ? 120 : 60, accent))) graphics.FillEllipse(dot, 118, 34, 4, 4);
                graphics.Restore(state);
            }
        }

        private sealed class ScreenGlyph : Control
        {
            private readonly int glyph;
            private readonly float size;

            public ScreenGlyph(int glyph, float size)
            {
                this.glyph = glyph;
                this.size = size;
                Dock = DockStyle.Fill;
                Margin = Padding.Empty;
                Tag = "accent";
                SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer, true);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                float scale = e.Graphics.DpiX / 96f * size;
                using (var icon = NavGlyph(glyph, ForeColor, scale))
                    e.Graphics.DrawImageUnscaled(icon, (Width - icon.Width) / 2, (Height - icon.Height) / 2);
            }
        }

        private static TextBox SearchField(string label)
        {
            var input = new TextBox
            {
                AccessibleName = label, Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle,
                Margin = new Padding(0, 6, 8, 0)
            };
            return input;
        }

        private sealed class HudPreview : Control
        {
            private int opacity = 100;
            private readonly Font clock = UiFont(27, FontStyle.Bold);

            public HudPreview() { DoubleBuffered = true; ResizeRedraw = true; }

            public void SetOpacity(int value) { opacity = value; Invalidate(); }

            private static void DrawMiniButton(Graphics graphics, Rectangle box, Color fill, bool stop, float dpi)
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var path = RoundedPath(box, (int)(6 * dpi)))
                using (var brush = new SolidBrush(fill))
                    graphics.FillPath(brush, path);
                using (var brush = new SolidBrush(Color.White))
                {
                    if (stop)
                        graphics.FillRectangle(brush, box.Left + box.Width / 2 - 4, box.Top + box.Height / 2 - 4, 8, 8);
                    else
                    {
                        graphics.FillRectangle(brush, box.Left + box.Width / 2 - 5, box.Top + 5, 3, box.Height - 10);
                        graphics.FillRectangle(brush, box.Left + box.Width / 2 + 2, box.Top + 5, 3, box.Height - 10);
                    }
                }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var gfx = e.Graphics;
                float dpi = gfx.DpiX / 96f;
                int width = Math.Min(Width - 14, (int)(300 * dpi)), height = Math.Min(Height - 14, (int)(162 * dpi));
                if (width < 100 || height < 92) return;
                var rect = new Rectangle((Width - width) / 2, (Height - height) / 2, width, height);
                var background = new WindowPalette(BackColor.GetBrightness() < 0.45);
                gfx.SmoothingMode = SmoothingMode.AntiAlias;
                float alpha = opacity / 100f;
                var surface = Motion.Blend(BackColor, background.Background, alpha);
                var text = Motion.Blend(BackColor, ForeColor, alpha);
                var blue = Motion.Blend(BackColor, background.HudAccent, alpha);
                using (var body = RoundedPath(rect, (int)(13 * dpi)))
                using (var fill = new LinearGradientBrush(rect, Motion.Blend(surface, background.Subtle, 0.2f), surface, LinearGradientMode.Vertical))
                using (var edge = new Pen(Motion.Blend(BackColor, background.Outline, alpha)))
                {
                    gfx.FillPath(fill, body);
                    gfx.DrawPath(edge, body);
                }
                int padding = (int)(15 * dpi);
                int innerWidth = width - 2 * padding;
                int left = rect.Left + padding;
                var align = TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter;
                using (var fill = new SolidBrush(blue)) gfx.FillEllipse(fill, left, rect.Top + padding + 4, 8 * dpi, 8 * dpi);
                TextRenderer.DrawText(gfx, "专注中", Font, new Rectangle(left + (int)(15 * dpi), rect.Top + padding - 1, innerWidth - 16, Font.Height + 4), text, align);
                TextRenderer.DrawText(gfx, "00:26:00", clock, new Rectangle(left, rect.Top + height / 4, innerWidth, height / 3), text, align);
                int barY = rect.Top + height * 2 / 3;
                var bar = new Rectangle(left, barY, innerWidth, Math.Max(3, (int)(5 * dpi)));
                using (var fill = new SolidBrush(background.Subtle)) gfx.FillRectangle(fill, bar);
                using (var fill = new SolidBrush(blue)) gfx.FillRectangle(fill, bar.X, bar.Y, bar.Width * 56 / 100, bar.Height);
                int buttonY = barY + bar.Height + (int)(5 * dpi);
                var stopBox = new Rectangle(rect.Right - padding - (int)(34 * dpi), buttonY, (int)(34 * dpi), (int)(20 * dpi));
                var pauseBox = new Rectangle(stopBox.Left - (int)(40 * dpi), buttonY, (int)(34 * dpi), (int)(20 * dpi));
                DrawMiniButton(gfx, pauseBox, blue, false, dpi);
                DrawMiniButton(gfx, stopBox, Color.FromArgb(230, 32, 32), true, dpi);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) clock.Dispose();
                base.Dispose(disposing);
            }
        }

        private sealed class DistributionChart : Control
        {
            private KeyValuePair<string, long>[] slices = new KeyValuePair<string, long>[0];
            private Color accentColor, secondary;
            private int legendTop, legendLine;
            private string[] legendKeys = new string[0];
            private long[] legendValues = new long[0];
            private static readonly Font CaptionFont = UiFont(10.5f);

            public DistributionChart() { DoubleBuffered = true; ResizeRedraw = true; MouseMove += delegate(object sender, MouseEventArgs e) { ShowTip(e.Y); }; MouseLeave += delegate { tip.SetToolTip(this, null); }; }

            private readonly ToolTip tip = new ToolTip();
            private void ShowTip(int y)
            {
                if (legendKeys.Length == 0) return;
                int index = (y - legendTop) / Math.Max(1, legendLine);
                if (index < 0 || index >= legendKeys.Length) { tip.SetToolTip(this, null); return; }
                long total = legendValues.Sum();
                double share = 100 * legendValues[index] / (double)Math.Max(1, total);
                tip.SetToolTip(this, legendKeys[index] + "  " + share.ToString("0.#") + "%  ·  " + DisplayDuration(legendValues[index], false));
            }

            public void Render(KeyValuePair<string, long>[] values, Color accent, Color muted)
            {
                slices = values;
                AccessibleDescription = values.Length == 0 ? "暂无软件前台用时" : string.Join("，", values.Select(item => item.Key + " " + DisplayDuration(item.Value, false)));
                Colors(accent, muted);
            }

            public void Colors(Color accent, Color muted)
            {
                accentColor = accent;
                secondary = muted;
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                var graphics = e.Graphics;
                long total = slices.Sum(item => item.Value);
                var center = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding;
                int diameter = Math.Min(Height - 12, (int)(Width * 0.30f));
                if (total == 0 || diameter < 22)
                {
                    TextRenderer.DrawText(graphics, "暂无软件前台用时", Font, ClientRectangle, secondary, center);
                    return;
                }
                Color[] colors = { accentColor, Motion.Blend(accentColor, Color.RoyalBlue, 0.7f),
                    Motion.Blend(accentColor, Color.Navy, 0.45f), Motion.Blend(accentColor, secondary, 0.56f) };
                int thickness = Math.Max(5, diameter / 6);
                var ring = new Rectangle(8 + thickness / 2, (Height - diameter + thickness) / 2, diameter - thickness, diameter - thickness);
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                float angle = -90;
                int legendX = diameter + 15;
                Font legendFont = Font;
                int line = legendFont.Height + 1;
                int slots = Math.Max(1, (Height - 3) / line);
                var shown = slots < slices.Length ? slices.Take(slots - 1)
                    .Concat(new[] { new KeyValuePair<string, long>("其他", slices.Skip(slots - 1).Sum(item => item.Value)) }).ToArray() : slices;
                int nameSpace = Math.Max(48, Width - legendX - 13 - 64);
                if (shown.Any(item => TextRenderer.MeasureText(item.Key, legendFont).Width > nameSpace))
                {
                    legendFont = UiFont(10.5f);
                    line = legendFont.Height + 1;
                    slots = Math.Max(1, (Height - 3) / line);
                    shown = slots < slices.Length ? slices.Take(slots - 1)
                        .Concat(new[] { new KeyValuePair<string, long>("其他", slices.Skip(slots - 1).Sum(item => item.Value)) }).ToArray() : slices;
                }
                legendTop = Math.Max(0, (Height - line * shown.Length) / 2);
                legendLine = line;
                legendKeys = shown.Select(item => item.Key).ToArray();
                legendValues = shown.Select(item => item.Value).ToArray();
                for (int i = 0; i < shown.Length; i++)
                {
                    float sweep = 360f * (shown[i].Value / (float)total);
                    // A subpixel segment remains in the legend. GDI+ can reject tiny arcs.
                    if (sweep >= 0.5f)
                        using (var pen = new Pen(colors[i], thickness))
                        {
                            if (sweep > 359.5f) graphics.DrawEllipse(pen, ring);
                            else graphics.DrawArc(pen, ring, angle, sweep);
                        }
                    angle += sweep;
                    int x = legendX, y = legendTop + i * line;
                    using (var fill = new SolidBrush(colors[i])) graphics.FillRectangle(fill, x, y + (line - 8) / 2, 8, 8);
                    double share = 100 * shown[i].Value / (double)total;
                    string percent = share < 0.05 ? "<0.1%" : share.ToString("0.#") + "%";
                    int percentWidth = TextRenderer.MeasureText(percent, legendFont).Width;
                    var nameBounds = new Rectangle(x + 13, y, Math.Max(0, Width - x - percentWidth - 19), line);
                    TextRenderer.DrawText(graphics, shown[i].Key, legendFont, nameBounds, ForeColor,
                        TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                    TextRenderer.DrawText(graphics, percent, legendFont, new Rectangle(Width - percentWidth - 3, y, percentWidth, line), secondary,
                        TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
                }
            }
        }

    }
}