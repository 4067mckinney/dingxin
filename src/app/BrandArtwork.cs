using System.Drawing;
using System.Drawing.Drawing2D;

namespace StudyFocus
{
    internal static class BrandArtwork
    {
        internal static void DrawMark(Graphics graphics, Rectangle bounds, Color baseColor, Color highlight)
        {
            if (bounds.Width < 8 || bounds.Height < 8) return;
            var state = graphics.Save();
            try
            {
                graphics.TranslateTransform(bounds.X, bounds.Y);
                graphics.ScaleTransform(bounds.Width / 48f, bounds.Height / 48f);
                graphics.SmoothingMode = SmoothingMode.AntiAlias;

                var tile = new RectangleF(1, 1, 46, 46);
                using (var outline = new GraphicsPath())
                {
                    const int diameter = 22;
                    outline.AddArc(tile.Left, tile.Top, diameter, diameter, 180, 90);
                    outline.AddArc(tile.Right - diameter, tile.Top, diameter, diameter, 270, 90);
                    outline.AddArc(tile.Right - diameter, tile.Bottom - diameter, diameter, diameter, 0, 90);
                    outline.AddArc(tile.Left, tile.Bottom - diameter, diameter, diameter, 90, 90);
                    outline.CloseFigure();
                    using (var fill = new LinearGradientBrush(tile, Blend(baseColor, highlight, .20f), baseColor, 65f))
                        graphics.FillPath(fill, outline);
                    using (var edge = new Pen(Color.FromArgb(150, highlight), .85f)) graphics.DrawPath(edge, outline);
                }

                var orbit = new RectangleF(11, 11, 26, 26);
                using (var track = new Pen(Color.FromArgb(91, highlight), 2.5f)) graphics.DrawEllipse(track, orbit);
                using (var progress = new Pen(highlight, 2.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    graphics.DrawArc(progress, orbit, 112, 245);
                using (var core = new SolidBrush(Color.White)) graphics.FillEllipse(core, 21.3f, 21.3f, 5.4f, 5.4f);
                using (var point = new SolidBrush(Blend(highlight, Color.White, .65f)))
                    graphics.FillEllipse(point, 34.1f, 20.9f, 5.8f, 5.8f);
            }
            finally { graphics.Restore(state); }
        }


        internal static void DrawWhale(Graphics graphics, RectangleF box, Color body, Color accent)
        {
            var state = graphics.Save();
            try
            {
                graphics.TranslateTransform(box.X, box.Y);
                graphics.ScaleTransform(box.Width / 100f, box.Height / 60f);
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var path = new GraphicsPath())
                {
                    path.AddBezier(8, 34, 8, 20, 28, 11, 47, 15);
                    path.AddBezier(47, 15, 61, 18, 69, 22, 75, 28);
                    path.AddLine(75, 28, 89, 13);
                    path.AddLine(89, 13, 93, 30);
                    path.AddLine(93, 30, 99, 32);
                    path.AddLine(99, 32, 90, 39);
                    path.AddLine(90, 39, 95, 53);
                    path.AddLine(95, 53, 78, 42);
                    path.AddBezier(78, 42, 62, 50, 30, 53, 17, 46);
                    path.AddBezier(17, 46, 10, 43, 8, 38, 8, 34);
                    path.CloseFigure();
                    using (var brush = new SolidBrush(body)) graphics.FillPath(brush, path);
                }
                using (var eye = new SolidBrush(accent)) graphics.FillEllipse(eye, 23, 26, 4.5f, 4.5f);
                using (var pen = new Pen(Color.FromArgb(190, body), 2.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    graphics.DrawArc(pen, 24, 3, 14, 14, 210, 110);
                    graphics.DrawArc(pen, 32, 0, 14, 14, 215, 100);
                }
            }
            finally { graphics.Restore(state); }
        }        private static Color Blend(Color a, Color b, float amount)
        {
            return Color.FromArgb((int)(a.R + (b.R - a.R) * amount),
                (int)(a.G + (b.G - a.G) * amount), (int)(a.B + (b.B - a.B) * amount));
        }
    }
}
